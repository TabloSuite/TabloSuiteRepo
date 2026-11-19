using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Noten;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Noten;

public sealed record BulletinZeile(Guid Id, string Nummer, Guid EinschreibungId, string Schueler, string Klasse, string Periode, BulletinStatus Status,
    bool IstRectificatif, DateTime VeroeffentlichtAmUtc, string? Grund, string Moyenne, string Rang, int Uebergaben);

public sealed record EigenesBulletin(Guid Id, string Nummer, string Jahr, string Periode, string Klasse, string Moyenne, string Rang, string Distinktion,
    DateTime VeroeffentlichtAmUtc, bool IstRectificatif);

public sealed record LiveNote(string Titel, DateOnly? Datum, string Note, decimal Bareme);

/// <summary>Fach der Live-Noten; <see cref="Facultative"/> = Hinweis bei einer gewählten « Matière facultative » (Regel).</summary>
public sealed record LiveFach(string Fach, decimal Koeffizient, IReadOnlyList<LiveNote> Noten, string Mittel, string? Facultative = null);

public sealed record LiveNoten(bool Erlaubt, string? Klasse, IReadOnlyList<PeriodeAuswahl> Perioden, Guid? PeriodeId, IReadOnlyList<LiveFach> Faecher, string? Moyenne);

/// <summary>Öffentliche Prüfung: nur Schule, Name, Klasse, Periode, Moyenne und Status – nichts weiter.</summary>
/// <summary>Öffentliche QR-Prüfung: nur, was auch auf dem Papier steht (Nummer und Datum zum Vergleich, Nutzer 18.11.).</summary>
public sealed record BulletinPruefung(string Schule, string Schueler, string Klasse, string Periode, string Moyenne, BulletinStatus Status,
    string Nummer, string Datum, string? ErsetztNummer);

public sealed record KlassenStandZeile(Guid KlasseId, string Klasse, int Bewertungen, int Noten, int Erwartet, bool Veroeffentlicht)
{
    public int Prozent => Erwartet == 0 ? 0 : (int)Math.Round(Noten * 100m / Erwartet);
}

/// <summary>Zum PDF nötige Daten eines Bulletins (Rechte bereits geprüft).</summary>
public sealed record BulletinDruck(BulletinSnapshot Snapshot, Guid? LogoMediumId, Guid? FotoMediumId, BulletinStatus Status, string Dateiname);

/// <summary>
/// Lesen der Bulletins: Liste (Direktion; Professeur principal seine Klassen), eigene/Kinder (nur gültige), Live-Noten für Eltern
/// (nur wenn die Schule es erlaubt, Standard aus – Bauplan §11.10), öffentliche QR-Prüfung, PDF-Daten mit Rechteprüfung,
/// Vollständigkeit je Klasse (für das Cockpit der Direktion).
/// </summary>
public sealed partial class BulletinLeseService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, TimeProvider zeit)
{
    public async Task<(IReadOnlyList<BulletinZeile> Eintraege, bool Mehr)> SucheAsync(Guid akteurId, Guid jahrId, Guid? klasseId, Guid? periodeId,
        string? suche, string? filter, int ueberspringen, int anzahl = 50, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenratErfassen, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        var q = from b in db.Bulletins.AsNoTracking()
                join e in db.Einschreibungen on b.EinschreibungId equals e.Id
                join k in db.Klassen on e.KlasseId equals k.Id
                join p in db.Perioden on b.PeriodeId equals p.Id
                join u in db.Users on e.SchuelerId equals u.Id
                where b.SchuleId == a.SchuleId && p.SchuljahrId == jahrId
                select new { b, k, p, u };
        if (!a.IstDirektion) q = q.Where(x => x.k.KlassenlehrerId == a.Id);
        if (klasseId is { } kid) q = q.Where(x => x.k.Id == kid);
        if (periodeId is { } pid) q = q.Where(x => x.p.Id == pid);
        q = filter switch
        {
            "remplace" => q.Where(x => x.b.Status == BulletinStatus.Remplace),
            "retire" => q.Where(x => x.b.Status == BulletinStatus.Retire),
            "rectificatif" => q.Where(x => x.b.IstRectificatif),
            _ => q.Where(x => x.b.Status == BulletinStatus.Publie)
        };
        if (Eingabe.Suche(suche) is { } s)
        {
            var muster = $"%{s}%";
            q = q.Where(x => EF.Functions.Like(x.u.Nachname + " " + x.u.Vorname, muster) || EF.Functions.Like(x.u.Vorname + " " + x.u.Nachname, muster)
                             || EF.Functions.Like(x.b.Nummer, muster));
        }
        var seite = await Seite<BulletinRoh>.LadeAsync(q.OrderBy(x => x.k.Name).ThenBy(x => x.u.Nachname).ThenBy(x => x.u.Vorname).ThenBy(x => x.p.Beginn)
            .Select(x => new BulletinRoh(x.b.Id, x.b.Nummer, x.b.EinschreibungId, x.u.Vorname, x.u.Nachname, x.k.Name, x.p.Titel, x.b.Status, x.b.IstRectificatif,
                x.b.VeroeffentlichtAmUtc, x.b.Grund, x.b.SnapshotJson, db.BulletinUebergaben.Count(ue => ue.BulletinId == x.b.Id))), ueberspringen, anzahl, ct);
        return (seite.Eintraege.Select(r =>
        {
            var i = BulletinSnapshot.AusJson(r.Json).Inhalt;
            return new BulletinZeile(r.Id, r.Nummer, r.EinschreibungId, NotenFormat.Name(r.Vorname, r.Nachname), r.Klasse, r.Periode, r.Status, r.IstRectificatif,
                r.Zeit, r.Grund, i.MoyenneText, i.Rang, r.Uebergaben);
        }).ToList(), seite.Mehr);
    }

    private sealed record BulletinRoh(Guid Id, string Nummer, Guid EinschreibungId, string Vorname, string Nachname, string Klasse, string Periode,
        BulletinStatus Status, bool IstRectificatif, DateTime Zeit, string? Grund, string Json, int Uebergaben);

    /// <summary>Gültige Bulletins eines Schülers (er selbst oder ein Elternteil), neueste zuerst.</summary>
    public async Task<IReadOnlyList<EigenesBulletin>> EigeneAsync(Guid akteurId, Guid schuelerId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await PruefeSchuelerAsync(db, akteurId, schuelerId, ct);
        var liste = await (from b in db.Bulletins.AsNoTracking()
                           join e in db.Einschreibungen on b.EinschreibungId equals e.Id
                           join k in db.Klassen on e.KlasseId equals k.Id
                           join p in db.Perioden on b.PeriodeId equals p.Id
                           join j in db.Schuljahre on p.SchuljahrId equals j.Id
                           where e.SchuelerId == schuelerId && b.Status == BulletinStatus.Publie
                           orderby j.Beginn descending, p.Beginn descending
                           select new { b.Id, b.Nummer, Jahr = j.Bezeichnung, p.Titel, Klasse = k.Name, b.SnapshotJson, b.VeroeffentlichtAmUtc, b.IstRectificatif })
            .ToListAsync(ct);
        return liste.Select(x =>
        {
            var i = BulletinSnapshot.AusJson(x.SnapshotJson).Inhalt;
            return new EigenesBulletin(x.Id, x.Nummer, x.Jahr, x.Titel, x.Klasse, i.MoyenneText, i.Rang, i.DistinktionText, x.VeroeffentlichtAmUtc, x.IstRectificatif);
        }).ToList();
    }

    /// <summary>Live-Noten eines Kindes (nur Eltern, nur wenn die Schule es erlaubt) für eine Periode des laufenden Jahres.</summary>
    public async Task<LiveNoten> LiveNotenAsync(Guid akteurId, Guid schuelerId, Guid? periodeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var schuleId = await PruefeSchuelerAsync(db, akteurId, schuelerId, ct);
        var schule = await db.Schulen.AsNoTracking().FirstAsync(s => s.Id == schuleId, ct);
        if (!schule.ElternSehenNotenLive) return new LiveNoten(false, null, [], null, [], null);
        var jahr = await SchulAbfragen.LaufendesSchuljahrAsync(db, schuleId, ct);
        if (jahr is null) return new LiveNoten(true, null, [], null, [], null);
        var perioden = await NotenService.PeriodenAsync(db, jahr, zeit, ct);
        var periode = perioden.FirstOrDefault(p => p.Id == periodeId) ?? perioden.FirstOrDefault(p => p.Aktuell);
        var eigene = await SchulAbfragen.GueltigeEinschreibungen(db).AsNoTracking().Where(e => e.SchuelerId == schuelerId && e.SchuljahrId == jahr.Id)
            .OrderByDescending(e => e.Von).ToListAsync(ct);
        if (periode is null || eigene.Count == 0) return new LiveNoten(true, null, perioden, periode?.Id, [], null);
        var aktuell = eigene[0];
        var klasse = await db.Klassen.AsNoTracking().Where(k => k.Id == aktuell.KlasseId).Select(k => k.Name).FirstAsync(ct);
        var ids = eigene.Select(e => e.Id).ToList();
        var klassen = eigene.Select(e => e.KlasseId).Distinct().ToList();
        // Wahlfächer nur, wenn das Kind sie gewählt hat (sonst erscheinen sie gar nicht, Bauplan §5 Regel 2a).
        var faecher = await (from kf in db.Klassenfaecher.AsNoTracking()
                             join f in db.Faecher on kf.FachId equals f.Id
                             where kf.KlasseId == aktuell.KlasseId
                                   && (!kf.IstWahlfach || db.Fachwahlen.Any(w => w.KlassenfachId == kf.Id && w.SchuelerId == schuelerId))
                             orderby kf.Reihenfolge ?? f.Reihenfolge, f.Name
                             select new { kf.FachId, f.Name, kf.Koeffizient, Wahlfach = kf.IstWahlfach ? (WahlfachRegel?)kf.WahlfachRegel : null }).ToListAsync(ct);
        var bewertungen = await (from b in db.Bewertungen.AsNoTracking()
                                 join kf in db.Klassenfaecher on b.KlassenfachId equals kf.Id
                                 where b.PeriodeId == periode.Id && klassen.Contains(kf.KlasseId)
                                 orderby b.Datum
                                 select new
                                 {
                                     b.Id, kf.FachId, kf.KlasseId, b.Titel, b.Datum, b.Bareme, b.Gewicht,
                                     Note = db.Noten.Where(n => n.BewertungId == b.Id && ids.Contains(n.EinschreibungId)).Select(n => new { n.Status, n.Punkte }).FirstOrDefault()
                                 }).ToListAsync(ct);
        var ergebnis = faecher.Select(f =>
        {
            var liste = bewertungen.Where(b => b.FachId == f.FachId && (b.KlasseId == aktuell.KlasseId || b.Note != null)).ToList();
            var mittel = NotenRechner.Fachmittel(liste.Select(b => new Wertung(b.Bareme, b.Gewicht, b.Note?.Status, b.Note?.Punkte,
                b.KlasseId != aktuell.KlasseId || NotenService.Erwartet(b.Datum, aktuell.Von, aktuell.Bis))), schule.AbsenzOhneEntschuldigungZaehltNull);
            return (Fach: new LiveFach(f.Name, f.Koeffizient,
                liste.Select(b => new LiveNote(b.Titel, b.Datum, b.Note is null || b.Note.Status == NotenStatus.Leer ? "—" : NotenTexte.Note(b.Note.Status, b.Note.Punkte), b.Bareme)).ToList(),
                NotenTexte.Mittel(mittel), f.Wahlfach is { } r ? $"Matière facultative · {NotenTexte.WahlfachText(r)}" : null),
                Beitrag: new FachBeitrag(mittel, f.Koeffizient, f.Wahlfach));
        }).ToList();
        var moyenne = NotenRechner.MoyenneGenerale(ergebnis.Select(x => x.Beitrag));
        return new LiveNoten(true, klasse, perioden, periode.Id, ergebnis.Select(x => x.Fach).ToList(), NotenTexte.Mittel(moyenne));
    }

    /// <summary>Schüler selbst oder verknüpftes Kind (gleiche Schule); liefert die Schule.</summary>
    private async Task<Guid> PruefeSchuelerAsync(SkoulouDbContext db, Guid akteurId, Guid schuelerId, CancellationToken ct)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        Zugriff.Pruefe(akteur, Faehigkeit.NotenErfassen);
        var erlaubt = akteur.Rolle switch
        {
            Rollen.Schueler => akteur.Id == schuelerId,
            Rollen.Eltern => await SchulAbfragen.IstKindAsync(db, akteur.Id, schuelerId, ct),
            _ => false
        };
        var schule = await db.Users.Where(u => u.Id == schuelerId && u.Rolle == Rollen.Schueler).Select(u => u.SchuleId).FirstOrDefaultAsync(ct);
        if (!erlaubt || schule is null || schule != akteur.SchuleId) throw new KeinZugriffFehler();
        return schule.Value;
    }

    // ---------- Öffentliche Prüfung ----------

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex TokenMuster();

    /// <summary>Prüft einen QR-Token (64 Hex); unbekannt → null. Keine Rechte nötig, keine weiteren Personendaten.</summary>
    public async Task<BulletinPruefung?> PruefenAsync(string? token, CancellationToken ct = default)
    {
        var t = (token ?? string.Empty).Trim().ToLowerInvariant();
        if (!TokenMuster().IsMatch(t)) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var b = await db.Bulletins.AsNoTracking().Where(x => x.PruefToken == t)
            .Select(x => new { x.SnapshotJson, x.Status, x.Nummer, x.VeroeffentlichtAmUtc }).FirstOrDefaultAsync(ct);
        if (b is null) return null;
        var s = BulletinSnapshot.AusJson(b.SnapshotJson);
        var i = s.Inhalt;
        // Nummer und Datum wie gedruckt (Snapshot); ältere Snapshots ohne Dokumentdaten: aus der Zeile
        var datum = s.Dokument?.Datum ?? SkoulouKultur.Datum(DateOnly.FromDateTime(b.VeroeffentlichtAmUtc));
        return new BulletinPruefung(i.Schule.Name, $"{i.Schueler.Vorname} {i.Schueler.Nachname}".Trim(), i.Klasse, $"{i.Periode} · {i.Jahr}", i.MoyenneText, b.Status,
            s.Dokument?.Nummer ?? b.Nummer, datum, s.Dokument?.ErsetztNummer);
    }

    // ---------- PDF-Daten ----------

    /// <summary>Direktion: jedes Bulletin der Schule; Professeur principal: seine Klasse; Schüler/Eltern: nur gültige eigene.</summary>
    public async Task<BulletinDruck> DruckAsync(Guid akteurId, Guid bulletinId, CancellationToken ct = default)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        Zugriff.Pruefe(akteur, Faehigkeit.NotenErfassen);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var x = await (from b in db.Bulletins.AsNoTracking()
                       join e in db.Einschreibungen on b.EinschreibungId equals e.Id
                       join k in db.Klassen on e.KlasseId equals k.Id
                       where b.Id == bulletinId
                       select new { b, e.SchuelerId, k.KlassenlehrerId }).FirstOrDefaultAsync(ct) ?? throw new KeinZugriffFehler();
        if (akteur.SchuleId != x.b.SchuleId) throw new KeinZugriffFehler();
        var erlaubt = akteur.Rolle switch
        {
            Rollen.Direktor or Rollen.Koordinator => true,
            Rollen.Lehrer => x.KlassenlehrerId == akteur.Id,
            Rollen.Schueler => x.SchuelerId == akteur.Id && x.b.Status == BulletinStatus.Publie,
            Rollen.Eltern => x.b.Status == BulletinStatus.Publie && await SchulAbfragen.IstKindAsync(db, akteur.Id, x.SchuelerId, ct),
            _ => false
        };
        if (!erlaubt) throw new KeinZugriffFehler();
        return new BulletinDruck(BulletinSnapshot.AusJson(x.b.SnapshotJson), x.b.LogoMediumId, x.b.FotoMediumId, x.b.Status, $"bulletin-{x.b.Nummer}.pdf");
    }

    /// <summary>Alle gültigen Bulletins einer Klasse und Periode (Direktion, Professeur principal), sortiert nach Name.</summary>
    public async Task<IReadOnlyList<BulletinDruck>> KlassenDruckAsync(Guid akteurId, Guid klasseId, Guid periodeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (_, klasse, _) = await zugriff.KlasseFuerConseilAsync(db, akteurId, klasseId, ct);
        var periode = await NotenRechte.PeriodeAsync(db, klasse, periodeId, ct);
        var liste = await (from b in db.Bulletins.AsNoTracking()
                           join e in db.Einschreibungen on b.EinschreibungId equals e.Id
                           join u in db.Users on e.SchuelerId equals u.Id
                           where e.KlasseId == klasse.Id && b.PeriodeId == periode.Id && b.Status == BulletinStatus.Publie
                           select new { b, u.Nachname, u.Vorname }).ToListAsync(ct);
        if (liste.Count == 0) throw new FachFehler("Aucun bulletin publié pour cette classe et cette période.");
        return liste.OrderBy(x => NotenFormat.Name(x.Vorname, x.Nachname), StringComparer.Create(SkoulouKultur.Kultur, true))
            .Select(x => new BulletinDruck(BulletinSnapshot.AusJson(x.b.SnapshotJson), x.b.LogoMediumId, x.b.FotoMediumId, x.b.Status, x.b.Nummer))
            .ToList();
    }

    /// <summary>Entwurf eines Schülers (vor der Veröffentlichung): « BROUILLON — NON VALIDE », ohne QR.</summary>
    public async Task<BulletinDruck> EntwurfAsync(Guid akteurId, Guid klasseId, Guid periodeId, Guid einschreibungId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (_, klasse, _) = await zugriff.KlasseFuerConseilAsync(db, akteurId, klasseId, ct);
        var periode = await NotenRechte.PeriodeAsync(db, klasse, periodeId, ct);
        var b = await BulletinBerechnung.LadeAsync(db, klasse, periode, ct);
        var s = b.Kohorte.FirstOrDefault(k => k.EinschreibungId == einschreibungId) ?? throw new FachFehler("Cet élève ne fait pas partie de la classe à la fin de la période.");
        var heute = new SchulZeit(zeit, b.Schule.Zeitzone).Heute;
        var snapshot = new BulletinSnapshot(1, b.Inhalt(s), new SnapshotDokument("BROUILLON — NON VALIDE", SkoulouKultur.Datum(heute), null, null, false, null, true));
        return new BulletinDruck(snapshot, b.Schule.LogoMediumId, s.FotoMediumId, BulletinStatus.Publie, "brouillon.pdf");
    }

    // ---------- Cockpit ----------

    /// <summary>Vollständigkeit der Noten je Klasse in einer Periode (Standard: aktuelle) – für das Cockpit der Direktion.</summary>
    public async Task<IReadOnlyList<KlassenStandZeile>> VollstaendigkeitAsync(Guid akteurId, Guid jahrId, Guid? periodeId = null, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.NotenAllerFaecher, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        var pid = periodeId ?? (await NotenService.PeriodenAsync(db, jahr, zeit, ct)).FirstOrDefault(p => p.Aktuell)?.Id;
        if (pid is null) return [];
        var aktive = SchulAbfragen.AktiveEinschreibungen(db);
        var liste = await db.Klassen.AsNoTracking().Where(k => k.SchuljahrId == jahr.Id && k.Aktiv).OrderBy(k => k.Name)
            .Select(k => new
            {
                k.Id, k.Name,
                Bewertungen = db.Bewertungen.Count(b => b.PeriodeId == pid && db.Klassenfaecher.Any(kf => kf.Id == b.KlassenfachId && kf.KlasseId == k.Id)),
                Pflicht = db.Bewertungen.Count(b => b.PeriodeId == pid && db.Klassenfaecher.Any(kf => kf.Id == b.KlassenfachId && kf.KlasseId == k.Id && !kf.IstWahlfach)),
                Noten = db.Noten.Count(n => n.Status != NotenStatus.Leer && db.Bewertungen.Any(b => b.Id == n.BewertungId && b.PeriodeId == pid
                    && db.Klassenfaecher.Any(kf => kf.Id == b.KlassenfachId && kf.KlasseId == k.Id))),
                Schueler = aktive.Count(e => e.KlasseId == k.Id),
                Pub = db.KlassenVeroeffentlichungen.Any(v => v.KlasseId == k.Id && v.PeriodeId == pid)
            }).ToListAsync(ct);
        // « Matières facultatives »: je Bewertung nur die aktiven Schüler mit Wahl erwartet (ohne Wahlfach: keine weitere Abfrage).
        var wahlErwartet = new Dictionary<Guid, int>();
        if (liste.Any(k => k.Pflicht != k.Bewertungen))
        {
            var ids = liste.Where(k => k.Pflicht != k.Bewertungen).Select(k => k.Id).ToList();
            var jeFach = await (from kf in db.Klassenfaecher.AsNoTracking()
                                where ids.Contains(kf.KlasseId) && kf.IstWahlfach
                                select new
                                {
                                    kf.KlasseId,
                                    Bewertungen = db.Bewertungen.Count(b => b.PeriodeId == pid && b.KlassenfachId == kf.Id),
                                    Waehlende = db.Fachwahlen.Count(w => w.KlassenfachId == kf.Id && aktive.Any(e => e.KlasseId == kf.KlasseId && e.SchuelerId == w.SchuelerId))
                                }).ToListAsync(ct);
            wahlErwartet = jeFach.GroupBy(x => x.KlasseId).ToDictionary(g => g.Key, g => g.Sum(x => x.Bewertungen * x.Waehlende));
        }
        return liste.Select(k => new KlassenStandZeile(k.Id, k.Name, k.Bewertungen, k.Noten, k.Pflicht * k.Schueler + wahlErwartet.GetValueOrDefault(k.Id), k.Pub)).ToList();
    }
}
