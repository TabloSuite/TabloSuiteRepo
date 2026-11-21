using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Organisation;

public sealed record LehrauftragKurz(Guid Id, Guid LehrerId, string Lehrer, decimal Wochenstunden);

/// <param name="WahlfachGesperrt">Bulletins der Klasse veröffentlicht: Schalter und Regel sind für dieses Jahr fest.</param>
/// <param name="NotenOhneWahl">Erfasste Noten von Schülern ohne diese Wahl (zählen nicht, bleiben gespeichert) – Hinweis im Dialog.</param>
/// <param name="KoeffizientGesperrt">Koeffizient fest (<see cref="KoeffizientStand.Fest"/>).</param>
/// <param name="KoeffizientInRectification">Koeffizient ausnahmsweise änderbar (<see cref="KoeffizientStand.Rectification"/>).</param>
public sealed record KlassenfachZeile(Guid Id, Guid FachId, string Fach, string? Kuerzel, FachGruppe Gruppe, decimal Koeffizient,
    int Reihenfolge, IReadOnlyList<LehrauftragKurz> Lehrer, bool IstWahlfach = false, WahlfachRegel WahlfachRegel = WahlfachRegel.PunkteUeber10,
    int AnzahlGewaehlt = 0, bool WahlfachGesperrt = false, int NotenOhneWahl = 0, bool KoeffizientGesperrt = false, bool KoeffizientInRectification = false);

/// <summary>
/// Darf der Koeffizient einer Klasse noch geändert werden? (21.11.2026, eng begrenzt)
/// <list type="bullet">
/// <item><see cref="Frei"/>: noch keine Bulletins der Klasse veröffentlicht.</item>
/// <item><see cref="Rectification"/>: genau EINE Periode veröffentlicht und diese gerade in Rectification – typischer Fall:
/// der Tippfehler fällt beim ersten Bulletin auf; die erneute Veröffentlichung gibt die Bulletins mit dem richtigen
/// Koeffizienten aus. Nur der Koeffizient, nicht die Wahlfach-Regel und keine neuen Fächer.</item>
/// <item><see cref="Fest"/>: sonst – frühere Bulletins und Conseil müssen zur Moyenne des Jahres passen.</item>
/// </list>
/// </summary>
public enum KoeffizientStand
{
    Frei,
    Rectification,
    Fest
}

public sealed record LehrauftragZeile(Guid Id, Guid KlassenfachId, Guid KlasseId, string Klasse, string Fach, decimal Wochenstunden);

/// <summary>Wahlfach einer Klasse (Spalte der Ansicht « Matières facultatives »).</summary>
public sealed record WahlfachSpalte(Guid KlassenfachId, string Fach, WahlfachRegel Regel);

/// <summary>Schüler der Klasse mit seinen gewählten Wahlfächern (KlassenfachIds); <see cref="Gesperrt"/> = hat schon ein Bulletin.</summary>
public sealed record FachwahlSchueler(Guid SchuelerId, string Name, string? Matrikel, IReadOnlySet<Guid> Gewaehlt, bool Gesperrt = false);

/// <summary>Ansicht « Matières facultatives » einer Klasse: Wahlfächer × Schüler (Häkchen); <see cref="Sperrhinweis"/> bei gesperrten Schülern.</summary>
public sealed record FachwahlUebersicht(Guid KlasseId, string Klasse, bool Offen, IReadOnlyList<WahlfachSpalte> Faecher, IReadOnlyList<FachwahlSchueler> Schueler,
    string? Sperrhinweis = null);

/// <summary>
/// Klassenfächer (Koeffizienten) und Lehraufträge – der Lehrauftrag ist die einzige Quelle dafür, wer
/// welche Klasse sieht und benotet (Bauplan §4.2). Direktor und Koordinator dürfen selbst unterrichten.
/// </summary>
public sealed class UnterrichtService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff)
{
    public const decimal MaxKoeffizient = 100;
    public const decimal MaxWochenstunden = 40;

    /// <summary>Schalter/Regel nach der ersten Veröffentlichung der Klasse (wie Bewertungen: Struktur, auch in einer Rectification fest).</summary>
    public const string MeldungWahlfachGesperrt =
        "Des bulletins sont déjà publiés pour cette classe : la matière facultative et sa règle ne peuvent plus changer cette année.";

    /// <summary>
    /// Koeffizienten (auch per Kopie oder « Appliquer au niveau ») nach der ersten Veröffentlichung der Klasse: fest wie die
    /// Wahlfach-Regel (Klicktest 20.11.2026: sonst rechnet der Conseil der veröffentlichten Periode andere Moyennes als die
    /// Bulletins der Familien, und eine spätere Rectification ersetzt still alle Bulletins mit dem neuen Koeffizienten).
    /// Einzige Ausnahme (21.11.2026): die Rectification der ersten und einzigen veröffentlichten Periode (<see cref="KoeffizientStand"/>).
    /// </summary>
    public const string MeldungKoeffizientGesperrt =
        "Des bulletins sont déjà publiés pour cette classe : ses matières et coefficients ne peuvent plus changer cette année (seule exception : la rectification de la première période publiée).";

    /// <summary>Hinweis am Koeffizienten während der Ausnahme.</summary>
    public const string HinweisKoeffizientRectification = "Modifiable pendant la rectification de la première période";

    /// <summary>Wahl eines Schülers mit Bulletin (wie eine Note: nur während einer Rectification korrigierbar).</summary>
    public const string MeldungWahlGesperrt =
        "Des bulletins sont publiés pour cette classe : le choix d'un élève qui a déjà un bulletin ne peut être corrigé que pendant une rectification.";

    public async Task<IReadOnlyList<KlassenfachZeile>> KlassenfaecherAsync(Guid akteurId, Guid klasseId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
        var faecher = await (from kf in db.Klassenfaecher.AsNoTracking()
                             join f in db.Faecher on kf.FachId equals f.Id
                             where kf.KlasseId == klasseId
                             select new
                             {
                                 kf.Id, kf.FachId, f.Name, f.Kuerzel, f.Gruppe, kf.Koeffizient, Reihenfolge = kf.Reihenfolge ?? f.Reihenfolge,
                                 kf.IstWahlfach, kf.WahlfachRegel, Gewaehlt = db.Fachwahlen.Count(w => w.KlassenfachId == kf.Id),
                                 // Noten von Schülern ohne diese Wahl (vor dem Einschalten: alle Noten des Fachs) – zählen nicht, bleiben gespeichert.
                                 OhneWahl = (from n in db.Noten
                                             join b in db.Bewertungen on n.BewertungId equals b.Id
                                             join e in db.Einschreibungen on n.EinschreibungId equals e.Id
                                             where b.KlassenfachId == kf.Id && n.Status != NotenStatus.Leer
                                                   && !db.Fachwahlen.Any(w => w.KlassenfachId == kf.Id && w.SchuelerId == e.SchuelerId)
                                             select n.Id).Count()
                             })
            .ToListAsync(ct);
        var stand = await KoeffizientStandAsync(db, klasseId, ct);
        var gesperrt = stand != KoeffizientStand.Frei;
        var ids = faecher.Select(f => f.Id).ToList();
        var auftraege = await (from l in db.Lehrauftraege.AsNoTracking()
                               join u in db.Users on l.LehrerId equals u.Id
                               where ids.Contains(l.KlassenfachId)
                               orderby u.Nachname
                               select new { l.KlassenfachId, Kurz = new LehrauftragKurz(l.Id, l.LehrerId, u.Vorname + " " + u.Nachname, l.Wochenstunden) })
            .ToListAsync(ct);
        return faecher.OrderBy(f => f.Reihenfolge).ThenBy(f => f.Name)
            .Select(f => new KlassenfachZeile(f.Id, f.FachId, f.Name, f.Kuerzel, f.Gruppe, f.Koeffizient, f.Reihenfolge,
                auftraege.Where(x => x.KlassenfachId == f.Id).Select(x => x.Kurz).ToList(), f.IstWahlfach, f.WahlfachRegel, f.Gewaehlt,
                gesperrt, f.OhneWahl, stand == KoeffizientStand.Fest, stand == KoeffizientStand.Rectification))
            .ToList();
    }

    /// <summary>
    /// « Matière facultative » ein/aus und Regel für die Moyenne (Bauplan §5 Regel 2a). Bestehende Wahlen bleiben gespeichert
    /// (ausgeschaltet zählen sie nicht), damit ein versehentliches Ausschalten nichts verliert. Nach der ersten Veröffentlichung
    /// der Klasse gesperrt (Nutzer 19.11.2026): die Regel ändert die Moyenne wie ein Koeffizient – Eltern hätten sonst ein
    /// Bulletin, das die App später anders rechnet.
    /// </summary>
    public async Task WahlfachSetzenAsync(Guid akteurId, Guid klassenfachId, bool istWahlfach, WahlfachRegel regel, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        if (!Enum.IsDefined(regel)) throw new FachFehler("Choisissez comment la matière facultative compte dans la moyenne.", "regel");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var kf = await KlassenfachAsync(db, a, klassenfachId, ct);
        await PruefeJahrOffenAsync(db, await db.Klassen.Where(k => k.Id == kf.KlasseId).Select(k => k.SchuljahrId).FirstAsync(ct), ct);
        if (kf.IstWahlfach == istWahlfach && kf.WahlfachRegel == regel) return;
        if (await db.KlassenVeroeffentlichungen.AnyAsync(v => v.KlasseId == kf.KlasseId, ct)) throw new FachFehler(MeldungWahlfachGesperrt);
        kf.IstWahlfach = istWahlfach;
        kf.WahlfachRegel = regel;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Ansicht « Matières facultatives »: Wahlfächer der Klasse und ihre Schüler (aktiv bzw. am Jahresende) mit Häkchen.</summary>
    public async Task<FachwahlUebersicht> FachwahlenAsync(Guid akteurId, Guid klasseId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var klasse = await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
        var jahr = await db.Schuljahre.AsNoTracking().FirstAsync(j => j.Id == klasse.SchuljahrId, ct);
        var faecher = await (from kf in db.Klassenfaecher.AsNoTracking()
                             join f in db.Faecher on kf.FachId equals f.Id
                             where kf.KlasseId == klasse.Id && kf.IstWahlfach
                             orderby kf.Reihenfolge ?? f.Reihenfolge, f.Name
                             select new WahlfachSpalte(kf.Id, f.Name, kf.WahlfachRegel)).ToListAsync(ct);
        var schueler = await SchuelerDerKlasseAsync(db, klasse.Id, ct);
        var ids = faecher.Select(f => f.KlassenfachId).ToList();
        var wahlen = await db.Fachwahlen.AsNoTracking().Where(w => ids.Contains(w.KlassenfachId)).Select(w => new { w.KlassenfachId, w.SchuelerId }).ToListAsync(ct);
        var gesperrt = await GesperrteWahlenAsync(db, klasse.Id, ct);
        return new FachwahlUebersicht(klasse.Id, klasse.Name, jahr.Status != SchuljahrStatus.Abgeschlossen, faecher,
            schueler.Select(s => new FachwahlSchueler(s.Id, s.Name, s.Matrikel,
                wahlen.Where(w => w.SchuelerId == s.Id).Select(w => w.KlassenfachId).ToHashSet(), gesperrt.Contains(s.Id))).ToList(),
            schueler.Any(s => gesperrt.Contains(s.Id)) ? MeldungWahlGesperrt : null);
    }

    /// <summary>
    /// Schüler der Klasse, deren Wahl nicht mehr geändert werden darf (Nutzer 19.11.2026): wer in dieser Klasse schon ein
    /// gültiges Bulletin hat (veröffentlicht oder berichtigt, nicht zurückgezogen) – außer während einer offenen Rectification
    /// der Klasse (wie Noten). Neu angekommene Schüler ohne Bulletin wählen immer.
    /// </summary>
    private static async Task<HashSet<Guid>> GesperrteWahlenAsync(SkoulouDbContext db, Guid klasseId, CancellationToken ct)
    {
        if (await db.KlassenVeroeffentlichungen.AnyAsync(v => v.KlasseId == klasseId && v.Status == VeroeffentlichungsStatus.EnRectification, ct)) return [];
        return (await (from b in db.Bulletins
                       join e in db.Einschreibungen on b.EinschreibungId equals e.Id
                       where e.KlasseId == klasseId && b.Status != BulletinStatus.Retire
                       select e.SchuelerId).Distinct().ToListAsync(ct)).ToHashSet();
    }

    /// <summary>
    /// Speichert die Häkchen der Ansicht « Matières facultatives » (vollständiger Stand für die Schüler der Klasse): fehlende
    /// Wahlen werden angelegt, abgewählte entfernt. Wahlen ehemaliger Schüler (Klassenwechsel) bleiben unberührt. Anzahl Änderungen.
    /// </summary>
    public async Task<int> FachwahlenSpeichernAsync(Guid akteurId, Guid klasseId, IReadOnlyCollection<(Guid KlassenfachId, Guid SchuelerId)> gewaehlt,
        CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        return await dbFactory.TransaktionAsync(async db =>
        {
            var klasse = await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
            await PruefeJahrOffenAsync(db, klasse.SchuljahrId, ct);
            var wahlfaecher = await db.Klassenfaecher.Where(kf => kf.KlasseId == klasse.Id && kf.IstWahlfach).Select(kf => kf.Id).ToListAsync(ct);
            var schueler = (await SchuelerDerKlasseAsync(db, klasse.Id, ct)).Select(s => s.Id).ToHashSet();
            var soll = gewaehlt.Distinct().ToHashSet();
            if (soll.Any(w => !wahlfaecher.Contains(w.KlassenfachId)))
                throw new FachFehler("Une des matières n'est pas (ou plus) facultative dans cette classe. Rechargez la page.");
            if (soll.Any(w => !schueler.Contains(w.SchuelerId)))
                throw new FachFehler("Un des élèves n'est pas (ou plus) inscrit dans cette classe. Rechargez la page.");
            var ist = await db.Fachwahlen.Where(w => wahlfaecher.Contains(w.KlassenfachId) && schueler.Contains(w.SchuelerId)).ToListAsync(ct);
            var weg = ist.Where(w => !soll.Contains((w.KlassenfachId, w.SchuelerId))).ToList();
            var neu = soll.Where(s => !ist.Any(w => w.KlassenfachId == s.KlassenfachId && w.SchuelerId == s.SchuelerId)).ToList();
            if (weg.Count + neu.Count > 0)
            {
                var gesperrt = await GesperrteWahlenAsync(db, klasse.Id, ct);
                if (weg.Any(w => gesperrt.Contains(w.SchuelerId)) || neu.Any(n => gesperrt.Contains(n.SchuelerId)))
                    throw new FachFehler(MeldungWahlGesperrt);
            }
            db.Fachwahlen.RemoveRange(weg);
            foreach (var (kfId, schuelerId) in neu)
                db.Fachwahlen.Add(new Fachwahl { SchuleId = a.SchuleId, KlassenfachId = kfId, SchuelerId = schuelerId, ErfasstVonId = a.Id });
            await db.SaveChangesAsync(ct);
            return weg.Count + neu.Count;
        }, ct);
    }

    /// <summary>
    /// Klassenwechsel (Bauplan §11.6): die Wahlen der alten Klasse gelten für dasselbe Fach der neuen Klasse, wenn es dort auch
    /// Wahlfach ist (sonst entfallen sie dort). Die Wahl in der alten Klasse bleibt für deren Perioden bestehen.
    /// </summary>
    internal static async Task FachwahlenUebernehmenAsync(SkoulouDbContext db, Guid schuleId, Guid schuelerId, Guid alteKlasseId, Guid neueKlasseId,
        Guid akteurId, CancellationToken ct)
    {
        var faecher = await (from w in db.Fachwahlen
                             join kf in db.Klassenfaecher on w.KlassenfachId equals kf.Id
                             where w.SchuelerId == schuelerId && kf.KlasseId == alteKlasseId && kf.IstWahlfach
                             select kf.FachId).ToListAsync(ct);
        if (faecher.Count == 0) return;
        var ziele = await db.Klassenfaecher.Where(kf => kf.KlasseId == neueKlasseId && kf.IstWahlfach && faecher.Contains(kf.FachId))
            .Select(kf => kf.Id).ToListAsync(ct);
        var vorhanden = await db.Fachwahlen.Where(w => w.SchuelerId == schuelerId && ziele.Contains(w.KlassenfachId)).Select(w => w.KlassenfachId).ToListAsync(ct);
        foreach (var ziel in ziele.Except(vorhanden))
            db.Fachwahlen.Add(new Fachwahl { SchuleId = schuleId, KlassenfachId = ziel, SchuelerId = schuelerId, ErfasstVonId = akteurId });
    }

    /// <summary>
    /// Jahreswechsel (Nutzer 19.11.2026): die Wahl des Vorjahres wird übernommen, wenn dasselbe Fach in der neuen Klasse auch
    /// Wahlfach ist – die Direktion ändert danach nur die Ausnahmen. Ein Durchgang für alle Schüler (keine Abfrage je Schüler);
    /// die neuen Klassenfächer müssen gespeichert sein. Anzahl übernommener Wahlen.
    /// </summary>
    internal static async Task<int> FachwahlenVorjahrUebernehmenAsync(SkoulouDbContext db, Guid schuleId,
        IReadOnlyCollection<(Guid SchuelerId, Guid AlteKlasseId, Guid NeueKlasseId)> wechsel, Guid akteurId, CancellationToken ct)
    {
        if (wechsel.Count == 0) return 0;
        var alteKlassen = wechsel.Select(w => w.AlteKlasseId).Distinct().ToList();
        var schueler = wechsel.Select(w => w.SchuelerId).Distinct().ToList();
        var alt = (await (from w in db.Fachwahlen
                          join kf in db.Klassenfaecher on w.KlassenfachId equals kf.Id
                          where alteKlassen.Contains(kf.KlasseId) && kf.IstWahlfach && schueler.Contains(w.SchuelerId)
                          select new { w.SchuelerId, kf.KlasseId, kf.FachId }).ToListAsync(ct))
            .ToLookup(x => (x.SchuelerId, x.KlasseId), x => x.FachId);
        if (alt.Count == 0) return 0;
        var neueKlassen = wechsel.Select(w => w.NeueKlasseId).Distinct().ToList();
        var ziele = (await db.Klassenfaecher.Where(kf => neueKlassen.Contains(kf.KlasseId) && kf.IstWahlfach)
                .Select(kf => new { kf.Id, kf.KlasseId, kf.FachId }).ToListAsync(ct))
            .ToDictionary(kf => (kf.KlasseId, kf.FachId), kf => kf.Id);
        var n = 0;
        foreach (var (schuelerId, alteKlasseId, neueKlasseId) in wechsel.DistinctBy(w => w.SchuelerId))
            foreach (var fachId in alt[(schuelerId, alteKlasseId)])
                if (ziele.TryGetValue((neueKlasseId, fachId), out var ziel))
                {
                    db.Fachwahlen.Add(new Fachwahl { SchuleId = schuleId, KlassenfachId = ziel, SchuelerId = schuelerId, ErfasstVonId = akteurId });
                    n++;
                }
        return n;
    }

    private sealed record KlassenSchueler(Guid Id, string Name, string? Matrikel);

    /// <summary>Schüler der Klasse: aktive Einschreibungen (nach dem Jahresabschluss die beendeten), sortiert nach Name.</summary>
    private static async Task<List<KlassenSchueler>> SchuelerDerKlasseAsync(SkoulouDbContext db, Guid klasseId, CancellationToken ct)
    {
        var liste = await (from e in SchulAbfragen.GueltigeEinschreibungen(db).AsNoTracking()
                           join u in db.Users on e.SchuelerId equals u.Id
                           where e.KlasseId == klasseId && (e.Status == EinschreibungsStatus.Active || e.Status == EinschreibungsStatus.Terminee)
                           select new { u.Id, u.Vorname, u.Nachname, Matrikel = db.SchuelerProfile.Where(p => p.KontoId == u.Id).Select(p => p.Matrikel).FirstOrDefault() })
            .ToListAsync(ct);
        return liste.DistinctBy(s => s.Id)
            .Select(s => new KlassenSchueler(s.Id, $"{s.Nachname.ToUpperInvariant()} {s.Vorname}".Trim(), s.Matrikel))
            .OrderBy(s => s.Name, StringComparer.Create(Skoulou.Core.Werkzeuge.SkoulouKultur.Kultur, true)).ToList();
    }

    /// <summary>Fächer einer Klasse hinzufügen (vorhandene bleiben unverändert). Anzahl neu.</summary>
    public async Task<int> FaecherHinzufuegenAsync(Guid akteurId, Guid klasseId, IReadOnlyCollection<Guid> fachIds, decimal koeffizient = 1, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        var koeff = PruefeKoeffizient(koeffizient);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var klasse = await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
        await PruefeJahrOffenAsync(db, klasse.SchuljahrId, ct);
        var gueltig = await db.Faecher.Where(f => f.SchuleId == a.SchuleId && f.Aktiv && fachIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct);
        if (gueltig.Count != fachIds.Distinct().Count()) throw new FachFehler("Une des matières choisies est introuvable ou désactivée.");
        var vorhanden = await db.Klassenfaecher.Where(kf => kf.KlasseId == klasse.Id).Select(kf => kf.FachId).ToListAsync(ct);
        var neu = gueltig.Except(vorhanden).ToList();
        foreach (var fachId in neu)
            db.Klassenfaecher.Add(new Klassenfach { SchuleId = a.SchuleId, KlasseId = klasse.Id, FachId = fachId, Koeffizient = koeff });
        await db.SaveChangesAsync(ct);
        return neu.Count;
    }

    public async Task KoeffizientSetzenAsync(Guid akteurId, Guid klassenfachId, decimal koeffizient, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        var koeff = PruefeKoeffizient(koeffizient);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var kf = await KlassenfachAsync(db, a, klassenfachId, ct);
        await PruefeJahrOffenAsync(db, await db.Klassen.Where(k => k.Id == kf.KlasseId).Select(k => k.SchuljahrId).FirstAsync(ct), ct);
        if (kf.Koeffizient == koeff) return;
        if (await KoeffizientStandAsync(db, kf.KlasseId, ct) == KoeffizientStand.Fest) throw new FachFehler(MeldungKoeffizientGesperrt);
        kf.Koeffizient = koeff;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Darf der Koeffizient der Klasse geändert werden (<see cref="KoeffizientStand"/>)?</summary>
    internal static async Task<KoeffizientStand> KoeffizientStandAsync(SkoulouDbContext db, Guid klasseId, CancellationToken ct)
    {
        var veroeffentlicht = await db.KlassenVeroeffentlichungen.AsNoTracking().Where(v => v.KlasseId == klasseId)
            .Select(v => new { v.PeriodeId, v.Status }).ToListAsync(ct);
        if (veroeffentlicht.Count == 0) return KoeffizientStand.Frei;
        return veroeffentlicht.Select(v => v.PeriodeId).Distinct().Count() == 1 && veroeffentlicht.All(v => v.Status == VeroeffentlichungsStatus.EnRectification)
            ? KoeffizientStand.Rectification
            : KoeffizientStand.Fest;
    }

    /// <summary>Fach aus der Klasse entfernen, solange keine Noten/Appréciations und kein Stundenplan daran hängen.</summary>
    public async Task KlassenfachEntfernenAsync(Guid akteurId, Guid klassenfachId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        await dbFactory.TransaktionAsync(async db =>
        {
            var kf = await KlassenfachAsync(db, a, klassenfachId, ct);
            if (await db.Bewertungen.AnyAsync(b => b.KlassenfachId == kf.Id, ct) || await db.FachBemerkungen.AnyAsync(b => b.KlassenfachId == kf.Id, ct))
                throw new FachFehler("Cette matière a déjà des évaluations dans cette classe : elle ne peut plus être retirée.");
            var auftraege = await db.Lehrauftraege.Where(l => l.KlassenfachId == kf.Id).ToListAsync(ct);
            var auftragIds = auftraege.Select(l => l.Id).ToList();
            if (await db.StundenplanEintraege.AnyAsync(e => auftragIds.Contains(e.LehrauftragId), ct))
                throw new FachFehler("Cette matière figure dans l'emploi du temps de la classe : retirez d'abord ces cours.");
            db.Lehrauftraege.RemoveRange(auftraege);
            db.Fachwahlen.RemoveRange(await db.Fachwahlen.Where(w => w.KlassenfachId == kf.Id).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            db.Klassenfaecher.Remove(kf);
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>
    /// Fächer und Koeffizienten einer Klasse in eine andere übernehmen (fehlende Fächer werden ergänzt). Ist die Zielklasse in
    /// der Rectification ihrer ersten Periode (<see cref="KoeffizientStand.Rectification"/>), nur die Koeffizienten vorhandener Fächer.
    /// </summary>
    public async Task<int> KoeffizientenKopierenAsync(Guid akteurId, Guid quelleKlasseId, Guid zielKlasseId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        if (quelleKlasseId == zielKlasseId) throw new FachFehler("Choisissez une autre classe comme modèle.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.KlasseAsync(db, a, quelleKlasseId, ct);
        var ziel = await OrgAbfragen.KlasseAsync(db, a, zielKlasseId, ct);
        await PruefeJahrOffenAsync(db, ziel.SchuljahrId, ct);
        var stand = await KoeffizientStandAsync(db, ziel.Id, ct);
        if (stand == KoeffizientStand.Fest) throw new FachFehler(MeldungKoeffizientGesperrt);
        var anzahl = await KopiereInAsync(db, a.SchuleId, quelleKlasseId, ziel.Id, nurKoeffizienten: stand == KoeffizientStand.Rectification, ct);
        await db.SaveChangesAsync(ct);
        return anzahl;
    }

    /// <summary>
    /// Koeffizienten der Klasse auf alle anderen Klassen derselben Stufe im selben Jahr übertragen. Anzahl Klassen. Klassen mit
    /// veröffentlichten Bulletins bleiben unverändert – außer in der Rectification ihrer ersten Periode (dann nur Koeffizienten).
    /// </summary>
    public async Task<int> KoeffizientenAufStufeAsync(Guid akteurId, Guid quelleKlasseId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var quelle = await OrgAbfragen.KlasseAsync(db, a, quelleKlasseId, ct);
        if (quelle.StufeId is null) throw new FachFehler("Cette classe n'a pas de niveau : choisissez d'abord son niveau.");
        await PruefeJahrOffenAsync(db, quelle.SchuljahrId, ct);
        var ziele = await db.Klassen.Where(k => k.SchuljahrId == quelle.SchuljahrId && k.StufeId == quelle.StufeId && k.Id != quelle.Id && k.Aktiv)
            .Select(k => k.Id).ToListAsync(ct);
        var anzahl = 0;
        foreach (var ziel in ziele)
        {
            var stand = await KoeffizientStandAsync(db, ziel, ct);
            if (stand == KoeffizientStand.Fest) continue;   // MeldungKoeffizientGesperrt
            await KopiereInAsync(db, a.SchuleId, quelle.Id, ziel, nurKoeffizienten: stand == KoeffizientStand.Rectification, ct);
            anzahl++;
        }
        await db.SaveChangesAsync(ct);
        return anzahl;
    }

    private static async Task<int> KopiereInAsync(SkoulouDbContext db, Guid schuleId, Guid quelle, Guid ziel, bool nurKoeffizienten, CancellationToken ct)
    {
        var quellFaecher = await db.Klassenfaecher.AsNoTracking().Where(kf => kf.KlasseId == quelle).ToListAsync(ct);
        var zielFaecher = await db.Klassenfaecher.Where(kf => kf.KlasseId == ziel).ToListAsync(ct);
        foreach (var q in quellFaecher)
        {
            if (zielFaecher.FirstOrDefault(z => z.FachId == q.FachId) is { } z)
            {
                z.Koeffizient = q.Koeffizient;
                if (nurKoeffizienten) continue;   // Rectification der ersten Periode: Wahlfach-Regel und Fächer bleiben fest
                z.Reihenfolge = q.Reihenfolge;
                z.IstWahlfach = q.IstWahlfach;
                z.WahlfachRegel = q.WahlfachRegel;
            }
            else if (!nurKoeffizienten)
            {
                db.Klassenfaecher.Add(new Klassenfach
                {
                    SchuleId = schuleId, KlasseId = ziel, FachId = q.FachId, Koeffizient = q.Koeffizient, Reihenfolge = q.Reihenfolge,
                    IstWahlfach = q.IstWahlfach, WahlfachRegel = q.WahlfachRegel
                });
            }
        }
        return quellFaecher.Count;
    }

    // ---------------- Lehraufträge ----------------

    /// <summary>Lehrauftrag setzen oder Stunden ändern (Upsert über Klassenfach + Lehrkraft).</summary>
    public async Task<Guid> LehrauftragSetzenAsync(Guid akteurId, Guid klassenfachId, Guid lehrerId, decimal wochenstunden, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        var stunden = Math.Round(wochenstunden * 2, MidpointRounding.AwayFromZero) / 2;
        if (stunden < 0.5m || stunden > MaxWochenstunden) throw new FachFehler("Les heures par semaine doivent être comprises entre 0,5 et 40 (par demi-heure).", "stunden");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var kf = await KlassenfachAsync(db, a, klassenfachId, ct);
        await PruefeJahrOffenAsync(db, await db.Klassen.Where(k => k.Id == kf.KlasseId).Select(k => k.SchuljahrId).FirstAsync(ct), ct);
        var lehrer = await db.Users.AsNoTracking().Where(k => k.Id == lehrerId)
            .Select(k => new { k.SchuleId, k.Rolle, k.Status }).FirstOrDefaultAsync(ct) ?? throw new FachFehler("Cet enseignant est introuvable.");
        if (lehrer.SchuleId != a.SchuleId) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
        if (!OrgRollen.Lehrkraefte.Contains(lehrer.Rolle)) throw new FachFehler("Seuls les enseignants, la direction et la coordination peuvent enseigner.");
        if (lehrer.Status == KontoStatus.Archive) throw new FachFehler("Ce compte est archivé : réintégrez-le d'abord.");

        var auftrag = await db.Lehrauftraege.FirstOrDefaultAsync(l => l.KlassenfachId == kf.Id && l.LehrerId == lehrerId, ct);
        if (auftrag is null)
        {
            auftrag = new Lehrauftrag { SchuleId = a.SchuleId, KlassenfachId = kf.Id, LehrerId = lehrerId };
            db.Lehrauftraege.Add(auftrag);
        }
        auftrag.Wochenstunden = stunden;
        await db.SaveChangesAsync(ct);
        return auftrag.Id;
    }

    public async Task LehrauftragEntfernenAsync(Guid akteurId, Guid lehrauftragId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var auftrag = await db.Lehrauftraege.FirstOrDefaultAsync(l => l.Id == lehrauftragId, ct) ?? throw new FachFehler("Cette affectation est introuvable.");
        a.PruefeSchule(auftrag.SchuleId);
        if (await db.StundenplanEintraege.AnyAsync(e => e.LehrauftragId == auftrag.Id, ct))
            throw new FachFehler("Cette affectation figure dans un emploi du temps : retirez d'abord ces cours.");
        db.Lehrauftraege.Remove(auftrag);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Lehraufträge einer Lehrkraft in einem Schuljahr.</summary>
    public async Task<IReadOnlyList<LehrauftragZeile>> AuftraegeDesLehrersAsync(Guid akteurId, Guid lehrerId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.KontoAsync(db, a, lehrerId, ct, OrgRollen.Lehrkraefte);
        return await (from l in db.Lehrauftraege.AsNoTracking()
                      join kf in db.Klassenfaecher on l.KlassenfachId equals kf.Id
                      join k in db.Klassen on kf.KlasseId equals k.Id
                      join f in db.Faecher on kf.FachId equals f.Id
                      where l.LehrerId == lehrerId && k.SchuljahrId == jahrId
                      orderby k.Name, f.Reihenfolge
                      select new LehrauftragZeile(l.Id, kf.Id, k.Id, k.Name, f.Name, l.Wochenstunden))
            .ToListAsync(ct);
    }

    /// <summary>Alle Klassenfächer eines Jahres (für die Auswahl beim Zuordnen), Zusatz = bisherige Lehrkräfte.</summary>
    public async Task<IReadOnlyList<AuswahlEintrag>> KlassenfaecherDesJahresAsync(Guid akteurId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        var liste = await (from kf in db.Klassenfaecher.AsNoTracking()
                           join k in db.Klassen on kf.KlasseId equals k.Id
                           join f in db.Faecher on kf.FachId equals f.Id
                           where k.SchuljahrId == jahrId && k.Aktiv
                           orderby k.Name, f.Reihenfolge
                           select new
                           {
                               kf.Id, Klasse = k.Name, Fach = f.Name,
                               Lehrer = db.Lehrauftraege.Where(l => l.KlassenfachId == kf.Id)
                                   .Join(db.Users, l => l.LehrerId, u => u.Id, (l, u) => u.Nachname).ToList()
                           }).ToListAsync(ct);
        return liste.Select(x => new AuswahlEintrag(x.Id, $"{x.Klasse} · {x.Fach}",
            x.Lehrer.Count == 0 ? "sans enseignant" : string.Join(", ", x.Lehrer))).ToList();
    }

    private static decimal PruefeKoeffizient(decimal k)
    {
        var r = Math.Round(k, 2, MidpointRounding.AwayFromZero);
        if (r <= 0 || r > MaxKoeffizient) throw new FachFehler("Le coefficient doit être compris entre 0,01 et 100.", "koeffizient");
        return r;
    }

    private static async Task<Klassenfach> KlassenfachAsync(SkoulouDbContext db, SchulAkteur a, Guid id, CancellationToken ct)
    {
        var kf = await db.Klassenfaecher.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new FachFehler("Cette matière de classe est introuvable.");
        a.PruefeSchule(kf.SchuleId);
        return kf;
    }

    private static async Task PruefeJahrOffenAsync(SkoulouDbContext db, Guid jahrId, CancellationToken ct)
        => OrgAbfragen.PruefeOffen(await db.Schuljahre.AsNoTracking().FirstAsync(j => j.Id == jahrId, ct));
}
