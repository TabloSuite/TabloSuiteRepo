using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Organisation;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Organisation;

public sealed record PlanZeile(Guid Id, DateOnly GueltigAb, DateOnly? GueltigBis, bool Veroeffentlicht, int Stunden, string Label);

public sealed record StundeZeile(Guid Id, Guid LehrauftragId, DayOfWeek Wochentag, TimeOnly Beginn, TimeOnly Ende, string? Raum,
    string Fach, string? Kuerzel, string Lehrer, string Klasse, string? Konflikt = null);

public sealed record LehrauftragOption(Guid Id, string Fach, string Lehrer, decimal Soll, decimal Ist);

public sealed record PlanDetail(Guid Id, Guid KlasseId, string Klasse, string? KlassenRaum, string Jahr, DateOnly JahrBeginn, DateOnly JahrEnde,
    DateOnly GueltigAb, DateOnly? GueltigBis, bool Veroeffentlicht, bool JahrOffen, string Label,
    IReadOnlyList<StundeZeile> Stunden, IReadOnlyList<LehrauftragOption> Lehrauftraege)
{
    public int Konflikte => Stunden.Count(s => s.Konflikt is not null);
}

public sealed record StundeEingabe(Guid LehrauftragId, DayOfWeek Wochentag, TimeOnly? Beginn, TimeOnly? Ende, string? Raum);

/// <summary>Wochenansicht für Lehrkräfte, Schüler und Eltern (nur veröffentlichte Pläne).</summary>
public sealed record WochenAnsicht(string Titel, DateOnly Montag, IReadOnlyList<StundeZeile> Stunden, Guid? PlanId, string? Hinweis);

/// <summary>
/// Stundenpläne je Klasse (Wochenraster mit Gültigkeit): Entwürfe bearbeiten, Konflikte (Klasse, Lehrkraft,
/// Raum) prüfen, veröffentlichen/zurückziehen, kopieren (andere Klasse oder anderer Zeitraum), Ansichten je Rolle.
/// </summary>
public sealed class StundenplanService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff)
{
    public static readonly TimeOnly FruehesterBeginn = new(6, 0);
    public static readonly TimeOnly SpaetestesEnde = new(22, 0);

    public static string Label(DateOnly ab, DateOnly? bis, DateOnly jahrBeginn, DateOnly jahrEnde)
        => (ab <= jahrBeginn && (bis is null || bis >= jahrEnde)) ? "Toute l'année"
            : bis is null ? $"À partir du {SkoulouKultur.Datum(ab)}" : $"Du {SkoulouKultur.Datum(ab)} au {SkoulouKultur.Datum(bis.Value)}";

    // ---------------- Direktion ----------------

    public async Task<IReadOnlyList<PlanZeile>> PlaeneAsync(Guid akteurId, Guid klasseId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.StundenplanVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var klasse = await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
        var jahr = await db.Schuljahre.AsNoTracking().FirstAsync(j => j.Id == klasse.SchuljahrId, ct);
        var plaene = await db.Stundenplaene.AsNoTracking().Where(p => p.KlasseId == klasse.Id).OrderBy(p => p.GueltigAb)
            .Select(p => new { p.Id, p.GueltigAb, p.GueltigBis, p.Veroeffentlicht, Stunden = p.Eintraege.Count }).ToListAsync(ct);
        return plaene.Select(p => new PlanZeile(p.Id, p.GueltigAb, p.GueltigBis, p.Veroeffentlicht, p.Stunden,
            Label(p.GueltigAb, p.GueltigBis, jahr.Beginn, jahr.Ende))).ToList();
    }

    /// <summary>Alle Pläne eines Schuljahres (Vorlagen zum Kopieren), Label « Classe · Zeitraum ».</summary>
    public async Task<IReadOnlyList<AuswahlEintrag>> PlaeneDesJahresAsync(Guid akteurId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.StundenplanVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        var plaene = await (from p in db.Stundenplaene.AsNoTracking()
                            join k in db.Klassen on p.KlasseId equals k.Id
                            where k.SchuljahrId == jahr.Id
                            orderby k.Name, p.GueltigAb
                            select new { p.Id, Klasse = k.Name, p.GueltigAb, p.GueltigBis, p.Veroeffentlicht, Stunden = p.Eintraege.Count }).ToListAsync(ct);
        return plaene.Select(p => new AuswahlEintrag(p.Id, $"{p.Klasse} · {Label(p.GueltigAb, p.GueltigBis, jahr.Beginn, jahr.Ende)}",
            $"{p.Stunden} cours · {(p.Veroeffentlicht ? "publié" : "brouillon")}")).ToList();
    }

    /// <summary>Neuer Entwurf (Standard: ganzes Jahr bzw. Session der Klasse), optional als Kopie eines anderen Plans.</summary>
    public async Task<Guid> PlanAnlegenAsync(Guid akteurId, Guid klasseId, DateOnly? ab, DateOnly? bis, Guid? kopieVonPlanId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.StundenplanVerwalten, ct: ct);
        var planId = await dbFactory.TransaktionAsync(async db =>
        {
            var klasse = await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
            var jahr = await db.Schuljahre.FirstAsync(j => j.Id == klasse.SchuljahrId, ct);
            OrgAbfragen.PruefeOffen(jahr);
            var session = klasse.PeriodeId is { } pid ? await db.Perioden.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid, ct) : null;
            var von = ab ?? session?.Beginn ?? jahr.Beginn;
            var biss = bis ?? session?.Ende;
            PruefeZeitraum(jahr, von, biss);
            var plan = new Stundenplan { SchuleId = a.SchuleId, KlasseId = klasse.Id, GueltigAb = von, GueltigBis = biss };
            db.Stundenplaene.Add(plan);
            await db.SaveChangesAsync(ct);
            return plan.Id;
        }, ct);
        if (kopieVonPlanId is { } quelle) await KopierenAsync(akteurId, planId, quelle, ct);
        return planId;
    }

    public async Task PlanZeitraumAsync(Guid akteurId, Guid planId, DateOnly? ab, DateOnly? bis, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.StundenplanVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (plan, jahr) = await EntwurfAsync(db, a, planId, ct);
        if (ab is null) throw new FachFehler("Indiquez la date de début de validité.", "ab");
        PruefeZeitraum(jahr, ab.Value, bis);
        plan.GueltigAb = ab.Value;
        plan.GueltigBis = bis;
        await db.SaveChangesAsync(ct);
    }

    public async Task PlanLoeschenAsync(Guid akteurId, Guid planId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.StundenplanVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (plan, _) = await EntwurfAsync(db, a, planId, ct);
        db.StundenplanEintraege.RemoveRange(await db.StundenplanEintraege.Where(e => e.StundenplanId == plan.Id).ToListAsync(ct));
        db.Stundenplaene.Remove(plan);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PlanDetail> PlanAsync(Guid akteurId, Guid planId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.StundenplanVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var plan = await PlanLadenAsync(db, a, planId, ct);
        var klasse = await db.Klassen.AsNoTracking().FirstAsync(k => k.Id == plan.KlasseId, ct);
        var jahr = await db.Schuljahre.AsNoTracking().FirstAsync(j => j.Id == klasse.SchuljahrId, ct);
        var belegungen = await BelegungenAsync(db, a.SchuleId, jahr, plan.Id, ct);
        var eigene = await StundenAsync(db, e => e.StundenplanId == plan.Id, ct);
        var stunden = eigene.Select(s => s with
        {
            Konflikt = belegungen.FirstOrDefault(b => b.EintragId == s.Id) is { } b ? StundenplanKonflikte.Pruefe(b, belegungen) : null
        }).ToList();
        var auftraege = await (from l in db.Lehrauftraege.AsNoTracking()
                               join kf in db.Klassenfaecher on l.KlassenfachId equals kf.Id
                               join f in db.Faecher on kf.FachId equals f.Id
                               join u in db.Users on l.LehrerId equals u.Id
                               where kf.KlasseId == klasse.Id
                               orderby f.Reihenfolge, u.Nachname
                               select new { l.Id, Fach = f.Name, Lehrer = u.Vorname + " " + u.Nachname, l.Wochenstunden }).ToListAsync(ct);
        var optionen = auftraege.Select(x => new LehrauftragOption(x.Id, x.Fach, x.Lehrer, x.Wochenstunden,
            Math.Round(stunden.Where(s => s.LehrauftragId == x.Id).Sum(s => (decimal)(s.Ende - s.Beginn).TotalMinutes) / 60m, 2))).ToList();
        return new PlanDetail(plan.Id, klasse.Id, klasse.Name, klasse.Raum, jahr.Bezeichnung, jahr.Beginn, jahr.Ende, plan.GueltigAb, plan.GueltigBis,
            plan.Veroeffentlicht, jahr.Status != SchuljahrStatus.Abgeschlossen, Label(plan.GueltigAb, plan.GueltigBis, jahr.Beginn, jahr.Ende),
            stunden, optionen);
    }

    /// <summary>Stunde anlegen/ändern (nur im Entwurf); Konflikte mit Klasse, Lehrkraft oder Raum werden abgewiesen.</summary>
    public async Task<Guid> StundeSpeichernAsync(Guid akteurId, Guid planId, Guid? stundeId, StundeEingabe e, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.StundenplanVerwalten, ct: ct);
        if (e.Beginn is null || e.Ende is null) throw new FachFehler("Indiquez l'heure de début et de fin.", "beginn");
        if (e.Ende <= e.Beginn) throw new FachFehler("L'heure de fin doit être postérieure à l'heure de début.", "ende");
        if (e.Beginn < FruehesterBeginn || e.Ende > SpaetestesEnde) throw new FachFehler("Les cours ont lieu entre 06:00 et 22:00.", "beginn");
        if (!Enum.IsDefined(e.Wochentag)) throw new FachFehler("Choisissez un jour de la semaine.", "tag");
        var raum = Eingabe.Text(e.Raum, 80, "la salle");
        return await dbFactory.TransaktionAsync(async db =>
        {
            var (plan, jahr) = await EntwurfAsync(db, a, planId, ct);
            var auftrag = await (from l in db.Lehrauftraege
                                 join kf in db.Klassenfaecher on l.KlassenfachId equals kf.Id
                                 where l.Id == e.LehrauftragId
                                 select new { l.Id, kf.KlasseId }).FirstOrDefaultAsync(ct);
            if (auftrag is null || auftrag.KlasseId != plan.KlasseId)
                throw new FachFehler("Choisissez une matière enseignée dans cette classe (affectation).", "lehrauftrag");

            StundenplanEintrag stunde;
            if (stundeId is { } sid)
            {
                stunde = await db.StundenplanEintraege.FirstOrDefaultAsync(x => x.Id == sid && x.StundenplanId == plan.Id, ct)
                         ?? throw new FachFehler("Ce cours est introuvable.");
            }
            else
            {
                stunde = new StundenplanEintrag { StundenplanId = plan.Id };
                db.StundenplanEintraege.Add(stunde);
            }
            stunde.LehrauftragId = auftrag.Id;
            stunde.Wochentag = e.Wochentag;
            stunde.Beginn = e.Beginn.Value;
            stunde.Ende = e.Ende.Value;
            stunde.Raum = raum;

            var belegungen = await BelegungenAsync(db, a.SchuleId, jahr, plan.Id, ct, ohne: stunde.Id);
            var neu = await BelegungAsync(db, plan, jahr, stunde, ct);
            if (StundenplanKonflikte.Pruefe(neu, belegungen) is { } konflikt) throw new FachFehler(konflikt);
            await db.SaveChangesAsync(ct);
            return stunde.Id;
        }, ct);
    }

    public async Task StundeLoeschenAsync(Guid akteurId, Guid stundeId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.StundenplanVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var stunde = await db.StundenplanEintraege.FirstOrDefaultAsync(x => x.Id == stundeId, ct) ?? throw new FachFehler("Ce cours est introuvable.");
        await EntwurfAsync(db, a, stunde.StundenplanId, ct);
        db.StundenplanEintraege.Remove(stunde);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Veröffentlichen: mindestens eine Stunde, kein anderer veröffentlichter Plan der Klasse im selben Zeitraum, keine Konflikte.</summary>
    public async Task VeroeffentlichenAsync(Guid akteurId, Guid planId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.StundenplanVerwalten, ct: ct);
        await dbFactory.TransaktionAsync(async db =>
        {
            var (plan, jahr) = await EntwurfAsync(db, a, planId, ct);
            if (!await db.StundenplanEintraege.AnyAsync(e => e.StundenplanId == plan.Id, ct))
                throw new FachFehler("Ajoutez au moins un cours avant de publier.");
            var zeitraum = new Zeitraum(plan.GueltigAb, plan.GueltigBis ?? jahr.Ende);
            var andere = await db.Stundenplaene.AsNoTracking().Where(p => p.KlasseId == plan.KlasseId && p.Veroeffentlicht && p.Id != plan.Id).ToListAsync(ct);
            if (andere.FirstOrDefault(p => new Zeitraum(p.GueltigAb, p.GueltigBis ?? jahr.Ende).Ueberschneidet(zeitraum)) is { } doppelt)
                throw new FachFehler($"Un autre emploi du temps publié de cette classe couvre déjà cette période ({Label(doppelt.GueltigAb, doppelt.GueltigBis, jahr.Beginn, jahr.Ende).ToLowerInvariant()}). Retirez-le d'abord ou ajustez les dates.");
            var belegungen = await BelegungenAsync(db, a.SchuleId, jahr, plan.Id, ct);
            foreach (var b in belegungen.Where(b => b.PlanId == plan.Id))
                if (StundenplanKonflikte.Pruefe(b, belegungen) is { } konflikt) throw new FachFehler(konflikt);
            plan.Veroeffentlicht = true;
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task ZurueckziehenAsync(Guid akteurId, Guid planId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.StundenplanVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var plan = await PlanLadenAsync(db, a, planId, ct);
        OrgAbfragen.PruefeOffen(await (from k in db.Klassen join j in db.Schuljahre on k.SchuljahrId equals j.Id where k.Id == plan.KlasseId select j).FirstAsync(ct));
        plan.Veroeffentlicht = false;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Stunden eines anderen Plans in den Entwurf übernehmen (ersetzt dessen Stunden). Aus einer anderen Klasse wird
    /// je Fach der Lehrauftrag der Zielklasse genommen; Fächer ohne Lehrauftrag werden ausgelassen.
    /// </summary>
    public async Task<(int Kopiert, int Ausgelassen)> KopierenAsync(Guid akteurId, Guid zielPlanId, Guid quellPlanId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.StundenplanVerwalten, ct: ct);
        if (zielPlanId == quellPlanId) throw new FachFehler("Choisissez un autre emploi du temps comme modèle.");
        return await dbFactory.TransaktionAsync(async db =>
        {
            var (ziel, _) = await EntwurfAsync(db, a, zielPlanId, ct);
            var quelle = await PlanLadenAsync(db, a, quellPlanId, ct);
            var quellStunden = await (from e in db.StundenplanEintraege.AsNoTracking()
                                      join l in db.Lehrauftraege on e.LehrauftragId equals l.Id
                                      join kf in db.Klassenfaecher on l.KlassenfachId equals kf.Id
                                      where e.StundenplanId == quelle.Id
                                      select new { e.Wochentag, e.Beginn, e.Ende, e.Raum, e.LehrauftragId, l.LehrerId, kf.FachId }).ToListAsync(ct);
            var zielAuftraege = await (from l in db.Lehrauftraege.AsNoTracking()
                                       join kf in db.Klassenfaecher on l.KlassenfachId equals kf.Id
                                       where kf.KlasseId == ziel.KlasseId
                                       select new { l.Id, l.LehrerId, kf.FachId }).ToListAsync(ct);
            var gleicheKlasse = quelle.KlasseId == ziel.KlasseId;
            var zielRaum = gleicheKlasse ? null : await db.Klassen.Where(k => k.Id == ziel.KlasseId).Select(k => k.Raum).FirstAsync(ct);

            db.StundenplanEintraege.RemoveRange(await db.StundenplanEintraege.Where(e => e.StundenplanId == ziel.Id).ToListAsync(ct));
            int kopiert = 0, ausgelassen = 0;
            foreach (var s in quellStunden)
            {
                var auftrag = gleicheKlasse
                    ? zielAuftraege.FirstOrDefault(x => x.Id == s.LehrauftragId)
                    : zielAuftraege.FirstOrDefault(x => x.FachId == s.FachId && x.LehrerId == s.LehrerId) ?? zielAuftraege.FirstOrDefault(x => x.FachId == s.FachId);
                if (auftrag is null) { ausgelassen++; continue; }
                db.StundenplanEintraege.Add(new StundenplanEintrag
                {
                    StundenplanId = ziel.Id, LehrauftragId = auftrag.Id, Wochentag = s.Wochentag, Beginn = s.Beginn, Ende = s.Ende,
                    Raum = gleicheKlasse ? s.Raum : zielRaum
                });
                kopiert++;
            }
            await db.SaveChangesAsync(ct);
            return (kopiert, ausgelassen);
        }, ct);
    }

    // ---------------- Ansichten (Lehrkraft, Schüler, Eltern) ----------------

    /// <summary>Eigene Stunden einer Lehrkraft in der Woche ab Montag (veröffentlichte Pläne).</summary>
    public async Task<WochenAnsicht> LehrerWocheAsync(Guid akteurId, DateOnly tag, CancellationToken ct = default)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        Zugriff.Pruefe(akteur, Faehigkeit.NotenErfassen);
        if (!OrgRollen.Lehrkraefte.Contains(akteur.Rolle)) throw new KeinZugriffFehler();
        var montag = Wochentage.Montag(tag);
        var sonntag = montag.AddDays(6);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var stunden = await StundenAsync(db, e => db.Lehrauftraege.Any(l => l.Id == e.LehrauftragId && l.LehrerId == akteurId)
            && db.Stundenplaene.Any(p => p.Id == e.StundenplanId && p.Veroeffentlicht && p.GueltigAb <= sonntag && (p.GueltigBis == null || p.GueltigBis >= montag)), ct);
        return new WochenAnsicht($"Emploi du temps de {akteur.Name}", montag, stunden, null,
            stunden.Count == 0 ? "Aucun cours publié pour vous cette semaine." : null);
    }

    /// <summary>Stundenplan der Klasse eines Schülers (selbst, Elternteil oder Direktion) in der Woche ab Montag.</summary>
    public async Task<WochenAnsicht> SchuelerWocheAsync(Guid akteurId, Guid schuelerId, DateOnly tag, CancellationToken ct = default)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        Zugriff.Pruefe(akteur, Faehigkeit.NotenErfassen);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await PruefeSchuelerSichtAsync(db, akteur, schuelerId, ct);
        var montag = Wochentage.Montag(tag);
        var sonntag = montag.AddDays(6);
        var einschreibung = await SchulAbfragen.EinschreibungenDesSchuelersImZeitraum(db, schuelerId, montag, sonntag).FirstOrDefaultAsync(ct);
        if (einschreibung is null)
            return new WochenAnsicht("Emploi du temps", montag, [], null, "L'élève n'est inscrit dans aucune classe cette semaine.");
        var klasse = await db.Klassen.AsNoTracking().Where(k => k.Id == einschreibung.KlasseId).Select(k => k.Name).FirstAsync(ct);
        var plan = await db.Stundenplaene.AsNoTracking()
            .Where(p => p.KlasseId == einschreibung.KlasseId && p.Veroeffentlicht && p.GueltigAb <= sonntag && (p.GueltigBis == null || p.GueltigBis >= montag))
            .OrderByDescending(p => p.GueltigAb).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        if (plan is null)
            return new WochenAnsicht($"Emploi du temps · {klasse}", montag, [], null, "Aucun emploi du temps publié pour cette semaine.");
        var stunden = await StundenAsync(db, e => e.StundenplanId == plan, ct);
        return new WochenAnsicht($"Emploi du temps · {klasse}", montag, stunden, plan, null);
    }

    /// <summary>Daten für das PDF eines Plans (Direktion: jeder Plan; sonst nur veröffentlicht und betroffen).</summary>
    public async Task<(string Titel, string Untertitel, string Schule, IReadOnlyList<StundeZeile> Stunden, bool MitKlasse)> PlanPdfAsync(
        Guid akteurId, Guid planId, CancellationToken ct = default)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        Zugriff.Pruefe(akteur, Faehigkeit.NotenErfassen);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var plan = await db.Stundenplaene.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct) ?? throw new FachFehler("Cet emploi du temps est introuvable.");
        if (akteur.SchuleId != plan.SchuleId) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
        if (!akteur.IstDirektion)
        {
            if (!plan.Veroeffentlicht) throw new KeinZugriffFehler();
            var kinder = SchulAbfragen.KinderDerEltern(db, akteur.Id);
            var erlaubt = akteur.Rolle switch
            {
                Rollen.Lehrer => await db.StundenplanEintraege.AnyAsync(e => e.StundenplanId == plan.Id && db.Lehrauftraege.Any(l => l.Id == e.LehrauftragId && l.LehrerId == akteur.Id), ct),
                Rollen.Schueler => await SchulAbfragen.GueltigeEinschreibungen(db).AnyAsync(e => e.SchuelerId == akteur.Id && e.KlasseId == plan.KlasseId, ct),
                Rollen.Eltern => await SchulAbfragen.GueltigeEinschreibungen(db).AnyAsync(e => e.KlasseId == plan.KlasseId && kinder.Contains(e.SchuelerId), ct),
                _ => false
            };
            if (!erlaubt) throw new KeinZugriffFehler();
        }
        var klasse = await db.Klassen.AsNoTracking().FirstAsync(k => k.Id == plan.KlasseId, ct);
        var jahr = await db.Schuljahre.AsNoTracking().FirstAsync(j => j.Id == klasse.SchuljahrId, ct);
        var schule = await db.Schulen.AsNoTracking().Where(s => s.Id == plan.SchuleId).Select(s => s.OffizielleBezeichnung ?? s.Name).FirstAsync(ct);
        var stunden = await StundenAsync(db, e => e.StundenplanId == plan.Id, ct);
        return ($"Emploi du temps · {klasse.Name}", $"{jahr.Bezeichnung} · {Label(plan.GueltigAb, plan.GueltigBis, jahr.Beginn, jahr.Ende)}", schule, stunden, false);
    }

    /// <summary>Daten für das PDF der eigenen Woche einer Lehrkraft.</summary>
    public async Task<(string Titel, string Untertitel, string Schule, IReadOnlyList<StundeZeile> Stunden, bool MitKlasse)> LehrerPdfAsync(
        Guid akteurId, DateOnly tag, CancellationToken ct = default)
    {
        var woche = await LehrerWocheAsync(akteurId, tag, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var schule = await db.Users.AsNoTracking().Where(k => k.Id == akteurId).Select(k => k.Schule!.OffizielleBezeichnung ?? k.Schule.Name).FirstAsync(ct);
        return (woche.Titel, $"Semaine du {SkoulouKultur.Datum(woche.Montag)}", schule, woche.Stunden, true);
    }

    // ---------------- Hilfen ----------------

    private static async Task PruefeSchuelerSichtAsync(SkoulouDbContext db, Akteur akteur, Guid schuelerId, CancellationToken ct)
    {
        switch (akteur.Rolle)
        {
            case Rollen.Schueler when akteur.Id == schuelerId: return;
            case Rollen.Eltern when await SchulAbfragen.IstKindAsync(db, akteur.Id, schuelerId, ct): return;
            case Rollen.Direktor or Rollen.Koordinator:
                if (await db.Users.AnyAsync(k => k.Id == schuelerId && k.SchuleId == akteur.SchuleId && k.Rolle == Rollen.Schueler, ct)) return;
                break;
        }
        throw new KeinZugriffFehler();
    }

    private static void PruefeZeitraum(Schuljahr jahr, DateOnly ab, DateOnly? bis)
    {
        var z = new Zeitraum(jahr.Beginn, jahr.Ende);
        if (!z.Enthaelt(ab) || (bis is { } b && (!z.Enthaelt(b) || b < ab)))
            throw new FachFehler($"La validité doit se situer dans l'année scolaire ({z}).", "ab");
    }

    private static async Task<Stundenplan> PlanLadenAsync(SkoulouDbContext db, SchulAkteur a, Guid planId, CancellationToken ct)
    {
        var plan = await db.Stundenplaene.FirstOrDefaultAsync(p => p.Id == planId, ct) ?? throw new FachFehler("Cet emploi du temps est introuvable.");
        a.PruefeSchule(plan.SchuleId);
        return plan;
    }

    private static async Task<(Stundenplan Plan, Schuljahr Jahr)> EntwurfAsync(SkoulouDbContext db, SchulAkteur a, Guid planId, CancellationToken ct)
    {
        var plan = await PlanLadenAsync(db, a, planId, ct);
        var jahr = await (from k in db.Klassen join j in db.Schuljahre on k.SchuljahrId equals j.Id where k.Id == plan.KlasseId select j).FirstAsync(ct);
        OrgAbfragen.PruefeOffen(jahr);
        if (plan.Veroeffentlicht) throw new FachFehler("Cet emploi du temps est publié : retirez la publication pour le modifier.");
        return (plan, jahr);
    }

    private static async Task<List<StundeZeile>> StundenAsync(SkoulouDbContext db, System.Linq.Expressions.Expression<Func<StundenplanEintrag, bool>> filter, CancellationToken ct)
        => await (from e in db.StundenplanEintraege.AsNoTracking().Where(filter)
                  join l in db.Lehrauftraege on e.LehrauftragId equals l.Id
                  join kf in db.Klassenfaecher on l.KlassenfachId equals kf.Id
                  join k in db.Klassen on kf.KlasseId equals k.Id
                  join f in db.Faecher on kf.FachId equals f.Id
                  join u in db.Users on l.LehrerId equals u.Id
                  orderby e.Wochentag, e.Beginn
                  select new StundeZeile(e.Id, l.Id, e.Wochentag, e.Beginn, e.Ende, e.Raum, f.Name, f.Kuerzel, u.Vorname + " " + u.Nachname, k.Name, null))
            .ToListAsync(ct);

    /// <summary>Belegungen der Schule im Jahr: eigener Plan (auch Entwurf) und alle veröffentlichten Pläne.</summary>
    private static async Task<List<Belegung>> BelegungenAsync(SkoulouDbContext db, Guid schuleId, Schuljahr jahr, Guid planId, CancellationToken ct, Guid? ohne = null)
        => (await (from e in db.StundenplanEintraege.AsNoTracking()
                   join p in db.Stundenplaene on e.StundenplanId equals p.Id
                   join l in db.Lehrauftraege on e.LehrauftragId equals l.Id
                   join kf in db.Klassenfaecher on l.KlassenfachId equals kf.Id
                   join k in db.Klassen on kf.KlasseId equals k.Id
                   join f in db.Faecher on kf.FachId equals f.Id
                   join u in db.Users on l.LehrerId equals u.Id
                   where p.SchuleId == schuleId && k.SchuljahrId == jahr.Id && (p.Veroeffentlicht || p.Id == planId) && e.Id != ohne
                   select new { e.Id, PlanId = p.Id, p.KlasseId, Klasse = k.Name, p.GueltigAb, p.GueltigBis, p.Veroeffentlicht, e.Wochentag, e.Beginn, e.Ende,
                       l.LehrerId, Lehrer = u.Vorname + " " + u.Nachname, Fach = f.Name, e.Raum }).ToListAsync(ct))
            .Select(x => new Belegung(x.Id, x.PlanId, x.KlasseId, x.Klasse, x.GueltigAb, x.GueltigBis ?? jahr.Ende, x.Veroeffentlicht, x.Wochentag,
                x.Beginn, x.Ende, x.LehrerId, x.Lehrer, x.Fach, x.Raum)).ToList();

    private static async Task<Belegung> BelegungAsync(SkoulouDbContext db, Stundenplan plan, Schuljahr jahr, StundenplanEintrag e, CancellationToken ct)
    {
        var info = await (from l in db.Lehrauftraege
                          join kf in db.Klassenfaecher on l.KlassenfachId equals kf.Id
                          join k in db.Klassen on kf.KlasseId equals k.Id
                          join f in db.Faecher on kf.FachId equals f.Id
                          join u in db.Users on l.LehrerId equals u.Id
                          where l.Id == e.LehrauftragId
                          select new { l.LehrerId, Lehrer = u.Vorname + " " + u.Nachname, Fach = f.Name, Klasse = k.Name }).FirstAsync(ct);
        return new Belegung(e.Id, plan.Id, plan.KlasseId, info.Klasse, plan.GueltigAb, plan.GueltigBis ?? jahr.Ende, plan.Veroeffentlicht,
            e.Wochentag, e.Beginn, e.Ende, info.LehrerId, info.Lehrer, info.Fach, e.Raum);
    }
}
