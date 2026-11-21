using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Organisation;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Services.Organisation;

/// <summary>Ergebnis einer Sammel-Einschreibung.</summary>
public sealed record SammelErgebnis(int Eingeschrieben, IReadOnlyList<string> Uebersprungen);

/// <summary>
/// Einschreibungen (Bauplan §4.2): höchstens eine aktive je Schüler; « Changer de classe » schließt die alte
/// (Bis = Vortag, Changee) und legt die neue an; « Départ » schließt mit Datum und Grund; eine irrtümliche
/// Einschreibung wird storniert (nicht gelöscht). Die aktuelle Klasse ergibt sich nur hieraus.
/// </summary>
public sealed class EinschreibungService(
    IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, SitzungsPruefung sitzungen, TimeProvider zeit)
{
    internal static void PruefeKlasseOffen(Klasse klasse, Schuljahr jahr)
    {
        if (!klasse.Aktiv) throw new FachFehler($"La classe {klasse.Name} est désactivée.");
        OrgAbfragen.PruefeOffen(jahr);
    }

    internal static void PruefeDatum(Schuljahr jahr, DateOnly datum)
    {
        if (datum < jahr.Beginn || datum > jahr.Ende)
            throw new FachFehler($"La date doit se situer dans l'année scolaire {jahr.Bezeichnung} ({SkoulouKultur.Datum(jahr.Beginn)} – {SkoulouKultur.Datum(jahr.Ende)}).", "datum");
    }

    /// <summary>
    /// Beginn einer neuen Einschreibung: angegeben (im Schuljahr) oder – Standard – Beginn des Schuljahres
    /// (<see cref="EinschreibungsBeginn"/>; Anlage, Import, Validierung einer Selbstregistrierung).
    /// </summary>
    internal static DateOnly Beginn(Schuljahr jahr, DateOnly? von)
    {
        if (von is not { } d) return EinschreibungsBeginn.Vorgabe(jahr.Beginn, jahr.Ende);
        PruefeDatum(jahr, d);
        return d;
    }

    /// <summary>Effektive Enden der (nicht stornierten) Einschreibungen der Schüler im Schuljahr (Session-Klassen enden mit der Session).</summary>
    private static async Task<List<(Guid SchuelerId, Guid Id, DateOnly Von, DateOnly? Ende)>> EndenAsync(
        SkoulouDbContext db, Guid schuljahrId, IReadOnlyCollection<Guid> schuelerIds, CancellationToken ct)
    {
        var liste = await (from e in db.Einschreibungen.AsNoTracking()
                           join k in db.Klassen on e.KlasseId equals k.Id
                           join p in db.Perioden on k.PeriodeId equals (Guid?)p.Id into pj
                           from p in pj.DefaultIfEmpty()
                           where e.SchuljahrId == schuljahrId && e.StorniertAmUtc == null && schuelerIds.Contains(e.SchuelerId)
                           select new { e.SchuelerId, e.Id, e.Von, e.Bis, SessionEnde = p == null ? (DateOnly?)null : p.Ende }).ToListAsync(ct);
        return liste.Select(x => (x.SchuelerId, x.Id, x.Von, SessionRegel.Ende(x.Bis, x.SessionEnde))).ToList();
    }

    /// <summary>
    /// Vorgabe für « Inscrire dans une classe » (einzeln oder mehrere): Beginn des Schuljahres bzw. der Tag nach dem Ende
    /// der letzten Einschreibung eines der Schüler in diesem Jahr (Départ, Session beendet).
    /// </summary>
    public async Task<DateOnly> VorgabeBeginnAsync(Guid akteurId, Guid schuljahrId, IReadOnlyCollection<Guid> schuelerIds, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var jahr = await OrgAbfragen.SchuljahrAsync(db, a, schuljahrId, ct);
        var ids = schuelerIds.Distinct().Take(500).ToList();
        var enden = await EndenAsync(db, jahr.Id, ids, ct);
        return EinschreibungsBeginn.Vorgabe(jahr.Beginn, jahr.Ende, enden.Select(e => e.Ende));
    }

    /// <summary>Eine neue Einschreibung darf keine frühere desselben Jahres überlappen.</summary>
    private static string? Ueberlappung(IEnumerable<(Guid SchuelerId, Guid Id, DateOnly Von, DateOnly? Ende)> enden, Guid schuelerId, DateOnly von)
        => enden.Where(e => e.SchuelerId == schuelerId && e.Ende is { } ende && ende >= von).Select(e => e.Ende).Max() is { } bis
            ? $"inscrit(e) jusqu'au {SkoulouKultur.Datum(bis)} : choisissez une date à partir du {SkoulouKultur.Datum(bis.AddDays(1))}"
            : null;

    /// <summary>
    /// Session-Klassen enden automatisch (<see cref="SessionRegel"/>): eine noch « aktive » Einschreibung, deren Session vor
    /// <paramref name="ab"/> endete, wird bei einer neuen Einschreibung zum Session-Ende abgeschlossen (« Terminée »).
    /// </summary>
    private static Task SessionEndenAbschliessenAsync(SkoulouDbContext db, IReadOnlyCollection<Guid> schuelerIds, DateOnly ab, CancellationToken ct)
        => SessionAbschluss.AbgelaufeneSchliessenAsync(db, db.Einschreibungen.Where(e => schuelerIds.Contains(e.SchuelerId)), ab, ct);

    /// <summary>Ende der Session einer Klasse (null = Klasse läuft das ganze Jahr).</summary>
    private static Task<DateOnly?> SessionEndeAsync(SkoulouDbContext db, Guid klasseId, CancellationToken ct)
        => (from k in db.Klassen
            where k.Id == klasseId
            join p in db.Perioden on k.PeriodeId equals (Guid?)p.Id
            select (DateOnly?)p.Ende).FirstOrDefaultAsync(ct);

    private async Task<(SchulAkteur A, Konto Schueler)> SchuelerAsync(SkoulouDbContext db, Guid akteurId, Guid schuelerId, CancellationToken ct)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        var konto = await OrgAbfragen.KontoAsync(db, a, schuelerId, ct, Rollen.Schueler);
        return (a, konto);
    }

    /// <summary>Schüler ohne aktive Einschreibung in eine Klasse einschreiben.</summary>
    public async Task<Guid> EinschreibenAsync(Guid akteurId, Guid schuelerId, Guid klasseId, DateOnly von, bool wiederholer, CancellationToken ct = default)
        => await dbFactory.TransaktionAsync(async db =>
        {
            var (a, konto) = await SchuelerAsync(db, akteurId, schuelerId, ct);
            var klasse = await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
            var jahr = await db.Schuljahre.FirstAsync(j => j.Id == klasse.SchuljahrId, ct);
            var e = await NeuAsync(db, a, konto, klasse, jahr, von, wiederholer, ct);
            await db.SaveChangesAsync(ct);
            return e.Id;
        }, ct);

    /// <summary>Mehrere Schüler (z. B. « Sans classe ») auf einmal einschreiben; bereits eingeschriebene werden übersprungen.</summary>
    public async Task<SammelErgebnis> MehrereEinschreibenAsync(Guid akteurId, Guid klasseId, IReadOnlyCollection<Guid> schuelerIds, DateOnly von, bool wiederholer, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        if (schuelerIds.Count == 0) throw new FachFehler("Sélectionnez au moins un élève.");
        if (schuelerIds.Count > 500) throw new FachFehler("Inscrivez au plus 500 élèves à la fois.");
        return await dbFactory.TransaktionAsync(async db =>
        {
            var klasse = await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
            var jahr = await db.Schuljahre.FirstAsync(j => j.Id == klasse.SchuljahrId, ct);
            PruefeKlasseOffen(klasse, jahr);
            PruefeDatum(jahr, von);
            var ids = schuelerIds.Distinct().ToList();
            var konten = await db.Users.Where(k => ids.Contains(k.Id)).ToListAsync(ct);
            if (konten.Any(k => k.SchuleId != a.SchuleId)) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
            await SessionEndenAbschliessenAsync(db, ids, von, ct);
            var mitKlasse = await SchulAbfragen.AktiveEinschreibungen(db).Where(e => ids.Contains(e.SchuelerId)).Select(e => e.SchuelerId).ToListAsync(ct);
            var enden = await EndenAsync(db, jahr.Id, ids, ct);
            var uebersprungen = new List<string>();
            var anzahl = 0;
            foreach (var konto in konten.OrderBy(k => k.Nachname))
            {
                if (konto.SchuleId != a.SchuleId) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
                if (konto.Rolle != Rollen.Schueler) continue;
                if (konto.Status != KontoStatus.Actif) { uebersprungen.Add($"{konto.VollerName} (compte non actif)"); continue; }
                if (mitKlasse.Contains(konto.Id)) { uebersprungen.Add($"{konto.VollerName} (déjà inscrit)"); continue; }
                if (Ueberlappung(enden, konto.Id, von) is { } grund) { uebersprungen.Add($"{konto.VollerName} ({grund})"); continue; }
                db.Einschreibungen.Add(new Einschreibung
                {
                    SchuleId = a.SchuleId, SchuelerId = konto.Id, SchuljahrId = jahr.Id, KlasseId = klasse.Id, Von = von,
                    Status = EinschreibungsStatus.Active, IstWiederholer = wiederholer
                });
                anzahl++;
            }
            if (anzahl > 0)
                audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.Eingeschrieben, nameof(Klasse), klasse.Id, $"{anzahl} élève(s) inscrit(s) en {klasse.Name} à partir du {SkoulouKultur.Datum(von)}.");
            await db.SaveChangesAsync(ct);
            return new SammelErgebnis(anzahl, uebersprungen);
        }, ct);
    }

    private async Task<Einschreibung> NeuAsync(SkoulouDbContext db, SchulAkteur a, Konto konto, Klasse klasse, Schuljahr jahr, DateOnly von, bool wiederholer, CancellationToken ct)
    {
        PruefeKlasseOffen(klasse, jahr);
        PruefeDatum(jahr, von);
        if (konto.Status != KontoStatus.Actif)
            throw new FachFehler(konto.Status == KontoStatus.EnAttente ? "Validez d'abord l'inscription de cet élève." : "Le compte de cet élève n'est pas actif.");
        await SessionEndenAbschliessenAsync(db, [konto.Id], von, ct);
        var aktiv = await (from e in SchulAbfragen.AktiveEinschreibungen(db)
                           join k in db.Klassen on e.KlasseId equals k.Id
                           where e.SchuelerId == konto.Id
                           select k.Name).FirstOrDefaultAsync(ct);
        if (aktiv is not null)
            throw new FachFehler($"{konto.VollerName} est déjà inscrit(e) en {aktiv}. Utilisez « Changer de classe ».");
        if (Ueberlappung(await EndenAsync(db, jahr.Id, [konto.Id], ct), konto.Id, von) is { } grund)
            throw new FachFehler($"{konto.VollerName} était {grund}.", "datum");
        var neu = new Einschreibung
        {
            SchuleId = a.SchuleId, SchuelerId = konto.Id, SchuljahrId = jahr.Id, KlasseId = klasse.Id, Von = von,
            Status = EinschreibungsStatus.Active, IstWiederholer = wiederholer
        };
        db.Einschreibungen.Add(neu);
        audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.Eingeschrieben, nameof(Konto), konto.Id,
            $"{konto.VollerName} inscrit(e) en {klasse.Name} ({jahr.Bezeichnung}) à partir du {SkoulouKultur.Datum(von)}{(wiederholer ? ", redoublant(e)" : "")}.");
        return neu;
    }

    /// <summary>« Changer de classe » ab Datum: alte Einschreibung endet am Vortag (Changee), neue beginnt am Datum.</summary>
    public async Task<Guid> KlasseWechselnAsync(Guid akteurId, Guid schuelerId, Guid neueKlasseId, DateOnly datum, CancellationToken ct = default)
        => await dbFactory.TransaktionAsync(async db =>
        {
            var (a, konto) = await SchuelerAsync(db, akteurId, schuelerId, ct);
            var alt = await SchulAbfragen.AktiveEinschreibungen(db).FirstOrDefaultAsync(e => e.SchuelerId == konto.Id, ct)
                      ?? throw new FachFehler("Cet élève n'est inscrit dans aucune classe : utilisez « Inscrire dans une classe ».");
            var neueKlasse = await OrgAbfragen.KlasseAsync(db, a, neueKlasseId, ct);
            if (neueKlasse.Id == alt.KlasseId) throw new FachFehler("L'élève est déjà dans cette classe.");
            if (neueKlasse.SchuljahrId != alt.SchuljahrId)
                throw new FachFehler("La nouvelle classe appartient à une autre année scolaire : utilisez le passage d'année.");
            var jahr = await db.Schuljahre.FirstAsync(j => j.Id == alt.SchuljahrId, ct);
            PruefeKlasseOffen(neueKlasse, jahr);
            PruefeDatum(jahr, datum);
            if (datum <= alt.Von)
                throw new FachFehler($"Le changement doit avoir lieu après le {SkoulouKultur.Datum(alt.Von)} (début de l'inscription actuelle). Pour corriger une erreur d'inscription, annulez l'inscription.", "datum");
            var altName = await db.Klassen.Where(k => k.Id == alt.KlasseId).Select(k => k.Name).FirstAsync(ct);

            // Session-Klasse: endete die Session schon vorher, endet die alte Einschreibung mit ihr.
            alt.Bis = SessionRegel.Ende(datum.AddDays(-1), await SessionEndeAsync(db, alt.KlasseId, ct));
            alt.Status = EinschreibungsStatus.Changee;
            await db.SaveChangesAsync(ct);

            var neu = new Einschreibung
            {
                SchuleId = a.SchuleId, SchuelerId = konto.Id, SchuljahrId = jahr.Id, KlasseId = neueKlasse.Id, Von = datum,
                Status = EinschreibungsStatus.Active, IstWiederholer = alt.IstWiederholer
            };
            db.Einschreibungen.Add(neu);
            await UnterrichtService.FachwahlenUebernehmenAsync(db, a.SchuleId, konto.Id, alt.KlasseId, neueKlasse.Id, a.Id, ct);
            audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.KlasseGewechselt, nameof(Konto), konto.Id,
                $"{konto.VollerName} : {altName} → {neueKlasse.Name} à partir du {SkoulouKultur.Datum(datum)}.");
            await db.SaveChangesAsync(ct);
            return neu.Id;
        }, ct);

    /// <summary>« Départ de l'élève »: letzte Schultag + Grund; auf Wunsch wird das Konto archiviert.</summary>
    public async Task AustrittAsync(Guid akteurId, Guid schuelerId, DateOnly letzterTag, string? grund, bool archivieren, CancellationToken ct = default)
    {
        var text = Eingabe.Pflicht(grund, 300, "le motif du départ", "grund");
        await dbFactory.TransaktionAsync(async db =>
        {
            var (a, konto) = await SchuelerAsync(db, akteurId, schuelerId, ct);
            var alt = await SchulAbfragen.AktiveEinschreibungen(db).FirstOrDefaultAsync(e => e.SchuelerId == konto.Id, ct)
                      ?? throw new FachFehler("Cet élève n'est inscrit dans aucune classe.");
            var jahr = await db.Schuljahre.FirstAsync(j => j.Id == alt.SchuljahrId, ct);
            OrgAbfragen.PruefeOffen(jahr);
            if (letzterTag < alt.Von || letzterTag > jahr.Ende)
                throw new FachFehler($"Le dernier jour doit se situer entre le {SkoulouKultur.Datum(alt.Von)} et le {SkoulouKultur.Datum(jahr.Ende)}.", "datum");
            alt.Bis = letzterTag;
            alt.Status = EinschreibungsStatus.Partie;
            alt.Austrittsgrund = text;
            audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.Austritt, nameof(Konto), konto.Id,
                $"Départ de {konto.VollerName} (dernier jour : {SkoulouKultur.Datum(letzterTag)}) : {text}.");
            if (archivieren)
            {
                KontoVerwaltungService.PruefeZiel(a, konto);
                konto.Status = KontoStatus.Archive;
                konto.AktiveSitzungId = Guid.NewGuid();
                audit.Erfasse(db, a.Id, a.SchuleId, AuditAktionen.KontoArchiviert, nameof(Konto), konto.Id, $"Élève {konto.VollerName} archivé(e) après son départ.");
            }
            await db.SaveChangesAsync(ct);
        }, ct);
        sitzungen.Vergessen(schuelerId);
    }

    /// <summary>
    /// Irrtümliche Einschreibung stornieren (zählt nicht mehr, auch nicht für die Abrechnung). Mit Noten oder
    /// Bulletins nicht möglich. Folgte sie auf einen Klassenwechsel, wird die vorherige Einschreibung wieder aktiv.
    /// </summary>
    public async Task StornierenAsync(Guid akteurId, Guid einschreibungId, string? grund, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        var text = Eingabe.Pflicht(grund, 300, "le motif de l'annulation", "grund");
        await dbFactory.TransaktionAsync(async db =>
        {
            var e = await db.Einschreibungen.FirstOrDefaultAsync(x => x.Id == einschreibungId, ct) ?? throw new FachFehler("Cette inscription est introuvable.");
            a.PruefeSchule(e.SchuleId);
            if (e.StorniertAmUtc is not null) throw new FachFehler("Cette inscription est déjà annulée.");
            var jahr = await db.Schuljahre.FirstAsync(j => j.Id == e.SchuljahrId, ct);
            OrgAbfragen.PruefeOffen(jahr);
            if (await db.Noten.AnyAsync(n => n.EinschreibungId == e.Id, ct) || await db.Bulletins.AnyAsync(b => b.EinschreibungId == e.Id, ct))
                throw new FachFehler("Cette inscription a déjà des notes ou des bulletins : utilisez « Changer de classe » ou « Départ de l'élève ».");
            var war = e.Status;
            db.Anwesenheiten.RemoveRange(await db.Anwesenheiten.Where(x => x.EinschreibungId == e.Id).ToListAsync(ct));
            e.StorniertAmUtc = zeit.GetUtcNow().UtcDateTime;
            e.StornoGrund = text;
            e.Status = EinschreibungsStatus.Terminee;
            await db.SaveChangesAsync(ct);

            var klasse = await db.Klassen.Where(k => k.Id == e.KlasseId).Select(k => k.Name).FirstAsync(ct);
            var konto = await db.Users.FirstAsync(k => k.Id == e.SchuelerId, ct);
            var hinweis = string.Empty;
            if (war == EinschreibungsStatus.Active)
            {
                var vorher = await db.Einschreibungen.Where(x => x.SchuelerId == e.SchuelerId && x.SchuljahrId == e.SchuljahrId && x.Id != e.Id
                        && x.StorniertAmUtc == null && x.Status == EinschreibungsStatus.Changee && x.Bis == e.Von.AddDays(-1))
                    .FirstOrDefaultAsync(ct);
                if (vorher is not null)
                {
                    vorher.Bis = null;
                    vorher.Status = EinschreibungsStatus.Active;
                    hinweis = " ; l'inscription précédente redevient active";
                }
            }
            audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.EinschreibungStorniert, nameof(Konto), konto.Id,
                $"Inscription de {konto.VollerName} en {klasse} annulée{hinweis} : {text}.");
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task WiederholerSetzenAsync(Guid akteurId, Guid einschreibungId, bool wiederholer, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.Einschreibungen.FirstOrDefaultAsync(x => x.Id == einschreibungId, ct) ?? throw new FachFehler("Cette inscription est introuvable.");
        a.PruefeSchule(e.SchuleId);
        OrgAbfragen.PruefeOffen(await db.Schuljahre.FirstAsync(j => j.Id == e.SchuljahrId, ct));
        e.IstWiederholer = wiederholer;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// « Corriger la date d'inscription » (Klicktest 20.11.2026: Schulstart mitten im Jahr): Beginn einer Einschreibung
    /// berichtigen – nur im offenen Schuljahr, innerhalb des Jahres, nicht vor dem Ende einer früheren Einschreibung
    /// desselben Jahres und nicht nach dem Ende dieser. Später setzen geht nur, solange davor keine Noten und keine
    /// Bulletins dieser Einschreibung liegen (sie fielen sonst aus der Einschreibung).
    /// </summary>
    public async Task BeginnKorrigierenAsync(Guid akteurId, Guid einschreibungId, DateOnly von, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        await dbFactory.TransaktionAsync(async db =>
        {
            var e = await db.Einschreibungen.FirstOrDefaultAsync(x => x.Id == einschreibungId, ct) ?? throw new FachFehler("Cette inscription est introuvable.");
            a.PruefeSchule(e.SchuleId);
            if (e.StorniertAmUtc is not null) throw new FachFehler("Cette inscription est annulée.");
            var jahr = await db.Schuljahre.FirstAsync(j => j.Id == e.SchuljahrId, ct);
            OrgAbfragen.PruefeOffen(jahr);
            PruefeDatum(jahr, von);
            if (von == e.Von) return;
            if (e.Bis is { } bis && von > bis)
                throw new FachFehler($"L'inscription se termine le {SkoulouKultur.Datum(bis)} : la date de début ne peut pas être postérieure.", "datum");
            var frueher = (await EndenAsync(db, jahr.Id, [e.SchuelerId], ct)).Where(x => x.Id != e.Id && x.Von < e.Von).ToList();
            if (Ueberlappung(frueher, e.SchuelerId, von) is { } grund)
                throw new FachFehler($"L'élève était {grund}.", "datum");
            if (von > e.Von)
            {
                var notenDavor = await (from n in db.Noten
                                        join b in db.Bewertungen on n.BewertungId equals b.Id
                                        join p in db.Perioden on b.PeriodeId equals p.Id
                                        where n.EinschreibungId == e.Id && n.Status != NotenStatus.Leer && ((b.Datum != null && b.Datum < von) || p.Ende < von)
                                        select n.Id).AnyAsync(ct);
                var bulletinsDavor = await (from bu in db.Bulletins
                                            join p in db.Perioden on bu.PeriodeId equals p.Id
                                            where bu.EinschreibungId == e.Id && p.Ende < von
                                            select bu.Id).AnyAsync(ct);
                if (notenDavor || bulletinsDavor)
                    throw new FachFehler($"Des notes ou des bulletins de cet élève sont antérieurs au {SkoulouKultur.Datum(von)} : la date ne peut pas être reculée jusque-là.", "datum");
            }
            var alt = e.Von;
            e.Von = von;
            var (name, klasse) = (await db.Users.Where(k => k.Id == e.SchuelerId).Select(k => k.Vorname + " " + k.Nachname).FirstAsync(ct),
                await db.Klassen.Where(k => k.Id == e.KlasseId).Select(k => k.Name).FirstAsync(ct));
            audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.EinschreibungsBeginnKorrigiert, nameof(Konto), e.SchuelerId,
                $"Inscription de {name.Trim()} en {klasse} : début corrigé du {SkoulouKultur.Datum(alt)} au {SkoulouKultur.Datum(von)}.");
            await db.SaveChangesAsync(ct);
        }, ct);
    }
}
