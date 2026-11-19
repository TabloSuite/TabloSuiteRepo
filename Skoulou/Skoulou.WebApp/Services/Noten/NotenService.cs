using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Noten;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Noten;

public sealed record FachAuswahl(Guid KlassenfachId, Guid FachId, string Fach, bool Schreibbar);

public sealed record KlasseAuswahl(Guid KlasseId, string Klasse, bool IstKlassenlehrer, IReadOnlyList<FachAuswahl> Faecher);

public sealed record PeriodeAuswahl(Guid Id, string Titel, PeriodenStatus Status, bool ZaehltFuerJahr, bool Aktuell);

public sealed record NotenAuswahl(IReadOnlyList<KlasseAuswahl> Klassen, IReadOnlyList<PeriodeAuswahl> Perioden);

public sealed record RasterSpalte(Guid BewertungId, string Titel, BewertungsArt Art, DateOnly? Datum, decimal Bareme, decimal Gewicht, int AnzahlNoten, Guid? KomponenteId);

public sealed record RasterZelle(Guid BewertungId, NotenStatus? Status, decimal? Punkte, bool Erwartet)
{
    public bool HatNote => Status is { } s && s != NotenStatus.Leer;
    public string Text => NotenTexte.Note(Status, Punkte);
}

public sealed record RasterZeile(Guid EinschreibungId, Guid SchuelerId, string Name, string? Matrikel, string? Hinweis, IReadOnlyList<RasterZelle> Zellen,
    string? Bemerkung, Mittel Mittel);

/// <summary>Raster eines Klassenfachs; <see cref="Wahlfach"/> = Regel einer « Matière facultative » (dann nur die Schüler mit Wahl).</summary>
public sealed record NotenRaster(Guid KlassenfachId, Guid PeriodeId, string Klasse, string Fach, string Periode, decimal Koeffizient, bool Schreibbar,
    string? Sperre, string? StrukturSperre, bool AbsentZaehltNull, IReadOnlyList<RasterSpalte> Spalten, IReadOnlyList<RasterZeile> Zeilen,
    WahlfachRegel? Wahlfach = null)
{
    public int Erwartet => Zeilen.Sum(z => z.Zellen.Count(c => c.Erwartet));
    public int Erfasst => Zeilen.Sum(z => z.Zellen.Count(c => c.Erwartet && c.HatNote));
    public bool Bearbeitbar => Schreibbar && Sperre is null;
}

/// <summary>Ergebnis einer gespeicherten Zelle: neue Zelle und neues Fachmittel der Zeile.</summary>
public sealed record ZellStand(RasterZelle Zelle, Mittel Mittel, bool Geaendert);

public sealed record NotenHistorie(DateTime ZeitUtc, string Von, string Alt, string Neu);

/// <summary>
/// Notenerfassung als Raster (Bauplan §7): Auswahl Klasse/Fach/Periode, Raster Schüler × Bewertungen, Speichern je Zelle
/// mit Historie (nur anhängen) und Audit bei Änderungen, Appréciation je Schüler. Sperren laut Sperrmatrix.
/// </summary>
public sealed class NotenService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, TimeProvider zeit)
{
    public async Task<NotenAuswahl> AuswahlAsync(Guid akteurId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.PersonalAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        var klassenQ = db.Klassen.AsNoTracking().Where(k => k.SchuljahrId == jahr.Id && k.Aktiv);
        var eigene = await SchulAbfragen.KlassenfaecherDesLehrers(db, a.Id).ToListAsync(ct);
        if (!a.IstDirektion)
        {
            var mitAuftrag = SchulAbfragen.KlassenDesLehrers(db, a.Id, jahr.Id);
            klassenQ = klassenQ.Where(k => mitAuftrag.Contains(k.Id) || k.KlassenlehrerId == a.Id);
        }
        var klassen = await klassenQ.OrderBy(k => k.Name).Select(k => new { k.Id, k.Name, k.KlassenlehrerId }).ToListAsync(ct);
        var ids = klassen.Select(k => k.Id).ToList();
        var faecher = await (from kf in db.Klassenfaecher.AsNoTracking()
                             join f in db.Faecher on kf.FachId equals f.Id
                             where ids.Contains(kf.KlasseId)
                             orderby kf.Reihenfolge ?? f.Reihenfolge, f.Name
                             select new { kf.Id, kf.KlasseId, kf.FachId, f.Name }).ToListAsync(ct);
        var ergebnis = klassen.Select(k =>
        {
            var pp = k.KlassenlehrerId == a.Id;
            var liste = faecher.Where(f => f.KlasseId == k.Id)
                .Select(f => new FachAuswahl(f.Id, f.FachId, f.Name, a.IstDirektion || eigene.Contains(f.Id)))
                .Where(f => f.Schreibbar || pp).ToList();
            return new KlasseAuswahl(k.Id, k.Name, pp, liste);
        }).Where(k => k.Faecher.Count > 0 || a.IstDirektion).ToList();
        return new NotenAuswahl(ergebnis, await PeriodenAsync(db, jahr, zeit, ct));
    }

    internal static async Task<IReadOnlyList<PeriodeAuswahl>> PeriodenAsync(SkoulouDbContext db, Schuljahr jahr, TimeProvider zeit, CancellationToken ct)
    {
        var heute = new SchulZeit(zeit).Heute;
        var perioden = await db.Perioden.AsNoTracking().Where(p => p.SchuljahrId == jahr.Id).OrderBy(p => p.Beginn).ThenBy(p => p.Nr).ToListAsync(ct);
        var aktuell = perioden.FirstOrDefault(p => p.ZaehltFuerJahr && p.Beginn <= heute && p.Ende >= heute)
                      ?? perioden.FirstOrDefault(p => p.Status == PeriodenStatus.Offen && p.ZaehltFuerJahr) ?? perioden.LastOrDefault();
        return perioden.Select(p => new PeriodeAuswahl(p.Id, p.Titel, p.Status, p.ZaehltFuerJahr, p.Id == aktuell?.Id)).ToList();
    }

    public async Task<NotenRaster> RasterAsync(Guid akteurId, Guid klassenfachId, Guid periodeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (_, k) = await zugriff.KlassenfachAsync(db, akteurId, klassenfachId, schreiben: false, ct);
        var periode = await NotenRechte.PeriodeAsync(db, k.Klasse, periodeId, ct);
        var status = await NotenSperren.StatusAsync(db, k.Klasse.Id, periode.Id, ct);
        var absentNull = await db.Schulen.Where(s => s.Id == k.Klasse.SchuleId).Select(s => s.AbsenzOhneEntschuldigungZaehltNull).FirstAsync(ct);

        var spalten = await db.Bewertungen.AsNoTracking().Where(b => b.KlassenfachId == k.Kf.Id && b.PeriodeId == periode.Id)
            .OrderBy(b => b.Datum == null).ThenBy(b => b.Datum).ThenBy(b => b.ErstelltAmUtc)
            .Select(b => new RasterSpalte(b.Id, b.Titel, b.Art, b.Datum, b.Bareme, b.Gewicht, db.Noten.Count(n => n.BewertungId == b.Id), b.KomponenteId))
            .ToListAsync(ct);
        var schueler = await SchuelerDerPeriodeAsync(db, k.Klasse, periode, ct);
        if (k.Kf.IstWahlfach)
        {
            // « Matière facultative »: nur wer sie gewählt hat, wird erwartet und benotet (Bauplan §5 Regel 2a).
            var waehlende = await WaehlendeAsync(db, k.Kf.Id, ct);
            schueler = schueler.Where(s => waehlende.Contains(s.SchuelerId)).ToList();
        }
        var bewIds = spalten.Select(s => s.BewertungId).ToList();
        var noten = await db.Noten.AsNoTracking().Where(n => bewIds.Contains(n.BewertungId))
            .Select(n => new { n.BewertungId, n.EinschreibungId, n.Status, n.Punkte }).ToListAsync(ct);
        var notenIndex = noten.ToDictionary(n => (n.BewertungId, n.EinschreibungId));
        var bemerkungen = await db.FachBemerkungen.AsNoTracking().Where(b => b.KlassenfachId == k.Kf.Id && b.PeriodeId == periode.Id)
            .ToDictionaryAsync(b => b.EinschreibungId, b => b.Text, ct);

        var zeilen = schueler.Select(s =>
        {
            var zellen = spalten.Select(sp =>
            {
                notenIndex.TryGetValue((sp.BewertungId, s.EinschreibungId), out var n);
                return new RasterZelle(sp.BewertungId, n?.Status, n?.Punkte, Erwartet(sp.Datum, s.Von, s.Bis));
            }).ToList();
            var mittel = NotenRechner.Fachmittel(zellen.Zip(spalten, (z, sp) => new Wertung(sp.Bareme, sp.Gewicht, z.Status, z.Punkte, z.Erwartet)), absentNull);
            return new RasterZeile(s.EinschreibungId, s.SchuelerId, s.Name, s.Matrikel, s.Hinweis, zellen, bemerkungen.GetValueOrDefault(s.EinschreibungId), mittel);
        }).ToList();

        return new NotenRaster(k.Kf.Id, periode.Id, k.Klasse.Name, k.Fach, periode.Titel, k.Kf.Koeffizient, k.Schreiben,
            NotenSperren.Noten(k.Jahr, periode, status), NotenSperren.Struktur(k.Jahr, periode, status), absentNull, spalten, zeilen,
            k.Kf.IstWahlfach ? k.Kf.WahlfachRegel : null);
    }

    public const string MeldungNichtGewaehlt = "Cet élève n'a pas choisi cette matière facultative : il n'y est pas noté.";

    /// <summary>Schüler mit Wahl dieses Klassenfachs (« Matière facultative »).</summary>
    private static async Task<HashSet<Guid>> WaehlendeAsync(SkoulouDbContext db, Guid klassenfachId, CancellationToken ct)
        => (await db.Fachwahlen.AsNoTracking().Where(w => w.KlassenfachId == klassenfachId).Select(w => w.SchuelerId).ToListAsync(ct)).ToHashSet();

    /// <summary>Wahlfach ohne Wahl des Schülers → Fehler (keine Note, keine Appréciation).</summary>
    private static async Task PruefeGewaehltAsync(SkoulouDbContext db, Klassenfach kf, Guid schuelerId, CancellationToken ct)
    {
        if (kf.IstWahlfach && !await db.Fachwahlen.AnyAsync(w => w.KlassenfachId == kf.Id && w.SchuelerId == schuelerId, ct))
            throw new FachFehler(MeldungNichtGewaehlt);
    }

    /// <summary>Ist eine Bewertung für die Einschreibung erwartet? Ohne Datum immer; sonst nur innerhalb der Einschreibung.</summary>
    public static bool Erwartet(DateOnly? datum, DateOnly von, DateOnly? bis) => datum is not { } d || (d >= von && (bis is null || d <= bis));

    internal sealed record SchuelerInPeriode(Guid EinschreibungId, Guid SchuelerId, string Name, string? Matrikel, DateOnly Von, DateOnly? Bis, string? Hinweis);

    /// <summary>Schüler, die im Zeitraum der Periode in der Klasse eingeschrieben waren (auch gewechselt/ausgetreten, mit Hinweis).</summary>
    internal static async Task<List<SchuelerInPeriode>> SchuelerDerPeriodeAsync(SkoulouDbContext db, Klasse klasse, Periode periode, CancellationToken ct)
    {
        var liste = await (from e in SchulAbfragen.EinschreibungenImZeitraum(db, klasse.SchuljahrId, periode.Beginn, periode.Ende).AsNoTracking()
                           join u in db.Users on e.SchuelerId equals u.Id
                           where e.KlasseId == klasse.Id
                           select new
                           {
                               e.Id, e.SchuelerId, u.Vorname, u.Nachname, e.Von, e.Bis, e.Status,
                               Matrikel = db.SchuelerProfile.Where(p => p.KontoId == u.Id).Select(p => p.Matrikel).FirstOrDefault()
                           }).ToListAsync(ct);
        return liste.Select(x => new SchuelerInPeriode(x.Id, x.SchuelerId, NotenFormat.Name(x.Vorname, x.Nachname), x.Matrikel, x.Von, x.Bis,
                x.Status switch
                {
                    EinschreibungsStatus.Changee when x.Bis is { } b => $"A changé de classe le {SkoulouKultur.Datum(b)}",
                    EinschreibungsStatus.Partie when x.Bis is { } b => $"Parti(e) le {SkoulouKultur.Datum(b)}",
                    _ when x.Von > periode.Beginn => $"Arrivé(e) le {SkoulouKultur.Datum(x.Von)}",
                    _ => null
                }))
            .OrderBy(s => s.Name, StringComparer.Create(SkoulouKultur.Kultur, true)).ToList();
    }

    /// <summary>Speichert eine Zelle (Autosave): Zahl, « abs », « aj », « disp » oder leer (Note entfernen). Gleiches Speichern = nichts.</summary>
    public async Task<ZellStand> NoteSpeichernAsync(Guid akteurId, Guid bewertungId, Guid einschreibungId, string? eingabe, CancellationToken ct = default)
    {
        return await dbFactory.TransaktionAsync(async db =>
        {
            var bewertung = await db.Bewertungen.FirstOrDefaultAsync(b => b.Id == bewertungId, ct) ?? throw new FachFehler("Cette évaluation est introuvable.");
            var (a, k) = await zugriff.KlassenfachAsync(db, akteurId, bewertung.KlassenfachId, schreiben: true, ct);
            var periode = await db.Perioden.FirstAsync(p => p.Id == bewertung.PeriodeId, ct);
            if (NotenSperren.Noten(k.Jahr, periode, await NotenSperren.StatusAsync(db, k.Klasse.Id, periode.Id, ct)) is { } sperre) throw new FachFehler(sperre);
            var einschreibung = await db.Einschreibungen.AsNoTracking().FirstOrDefaultAsync(e => e.Id == einschreibungId && e.StorniertAmUtc == null, ct);
            if (einschreibung is null || einschreibung.KlasseId != k.Klasse.Id || einschreibung.Von > periode.Ende || (einschreibung.Bis is { } bis && bis < periode.Beginn))
                throw new FachFehler("Cet élève n'est pas inscrit dans cette classe pendant la période.");
            await PruefeGewaehltAsync(db, k.Kf, einschreibung.SchuelerId, ct);

            var neu = NotenEingabe.Lese(eingabe, bewertung.Bareme);
            var note = await db.Noten.FirstOrDefaultAsync(n => n.BewertungId == bewertung.Id && n.EinschreibungId == einschreibungId, ct);
            var neuerStatus = neu.Leer ? NotenStatus.Leer : neu.Status;
            var geaendert = note is null ? !neu.Leer : note.Status != neuerStatus || note.Punkte != neu.Punkte;
            if (geaendert)
            {
                var jetzt = zeit.GetUtcNow().UtcDateTime;
                if (note is null)
                {
                    note = new Note { SchuleId = k.Klasse.SchuleId, BewertungId = bewertung.Id, EinschreibungId = einschreibungId };
                    db.Noten.Add(note);
                }
                else if (note.Status != NotenStatus.Leer)
                {
                    audit.Erfasse(db, a.Id, a.SchuleId, AuditAktionen.NoteGeaendert, nameof(Note), note.Id,
                        $"{k.Fach} · {k.Klasse.Name} · {bewertung.Titel} : {NotenTexte.Note(note.Status, note.Punkte)} → {(neu.Leer ? "retirée" : NotenTexte.Note(neu.Status, neu.Punkte))}");
                }
                db.NotenAenderungen.Add(new NotenAenderung
                {
                    SchuleId = note.SchuleId, NoteId = note.Id, AltePunkte = note.Punkte,
                    AlterStatus = db.Entry(note).State == EntityState.Added ? null : note.Status,
                    NeuePunkte = neu.Punkte, NeuerStatus = neuerStatus, GeaendertVonId = a.Id, ZeitUtc = jetzt
                });
                note.Status = neuerStatus;
                note.Punkte = neu.Punkte;
                note.ErfasstVonId = a.Id;
                await db.SaveChangesAsync(ct);
            }
            var mittel = await FachmittelAsync(db, bewertung.KlassenfachId, periode.Id, einschreibung, ct);
            return new ZellStand(new RasterZelle(bewertung.Id, note?.Status, note?.Punkte, Erwartet(bewertung.Datum, einschreibung.Von, einschreibung.Bis)), mittel, geaendert);
        }, ct);
    }

    private static async Task<Mittel> FachmittelAsync(SkoulouDbContext db, Guid klassenfachId, Guid periodeId, Einschreibung e, CancellationToken ct)
    {
        var absentNull = await db.Schulen.Where(s => s.Id == e.SchuleId).Select(s => s.AbsenzOhneEntschuldigungZaehltNull).FirstAsync(ct);
        var werte = await (from b in db.Bewertungen.AsNoTracking()
                           where b.KlassenfachId == klassenfachId && b.PeriodeId == periodeId
                           select new
                           {
                               b.Bareme, b.Gewicht, b.Datum,
                               Note = db.Noten.Where(n => n.BewertungId == b.Id && n.EinschreibungId == e.Id).Select(n => new { n.Status, n.Punkte }).FirstOrDefault()
                           }).ToListAsync(ct);
        return NotenRechner.Fachmittel(werte.Select(w => new Wertung(w.Bareme, w.Gewicht, w.Note?.Status, w.Note?.Punkte, Erwartet(w.Datum, e.Von, e.Bis))), absentNull);
    }

    /// <summary>Appréciation des Lehrers je Fach, Periode und Schüler (leer = entfernen).</summary>
    public async Task BemerkungSpeichernAsync(Guid akteurId, Guid klassenfachId, Guid periodeId, Guid einschreibungId, string? text, CancellationToken ct = default)
    {
        var t = NotenFormat.Text(text, 300, "L'appréciation");
        await dbFactory.TransaktionAsync(async db =>
        {
            var (a, k) = await zugriff.KlassenfachAsync(db, akteurId, klassenfachId, schreiben: true, ct);
            var periode = await NotenRechte.PeriodeAsync(db, k.Klasse, periodeId, ct);
            if (NotenSperren.Noten(k.Jahr, periode, await NotenSperren.StatusAsync(db, k.Klasse.Id, periode.Id, ct)) is { } sperre) throw new FachFehler(sperre);
            var schuelerId = await db.Einschreibungen.Where(e => e.Id == einschreibungId && e.KlasseId == k.Klasse.Id && e.StorniertAmUtc == null)
                .Select(e => (Guid?)e.SchuelerId).FirstOrDefaultAsync(ct) ?? throw new FachFehler("Cet élève n'est pas inscrit dans cette classe.");
            await PruefeGewaehltAsync(db, k.Kf, schuelerId, ct);
            var vorhanden = await db.FachBemerkungen.FirstOrDefaultAsync(b => b.KlassenfachId == k.Kf.Id && b.PeriodeId == periode.Id && b.EinschreibungId == einschreibungId, ct);
            if (t is null)
            {
                if (vorhanden is not null) db.FachBemerkungen.Remove(vorhanden);
            }
            else if (vorhanden is null)
            {
                db.FachBemerkungen.Add(new FachBemerkung { SchuleId = a.SchuleId, KlassenfachId = k.Kf.Id, PeriodeId = periode.Id, EinschreibungId = einschreibungId, Text = t, ErfasstVonId = a.Id });
            }
            else
            {
                vorhanden.Text = t;
                vorhanden.ErfasstVonId = a.Id;
            }
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>Historie einer Note (wer, wann, alt → neu), neueste zuerst.</summary>
    public async Task<IReadOnlyList<NotenHistorie>> HistorieAsync(Guid akteurId, Guid bewertungId, Guid einschreibungId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var kfId = await db.Bewertungen.Where(b => b.Id == bewertungId).Select(b => (Guid?)b.KlassenfachId).FirstOrDefaultAsync(ct)
                   ?? throw new FachFehler("Cette évaluation est introuvable.");
        await zugriff.KlassenfachAsync(db, akteurId, kfId, schreiben: false, ct);
        var liste = await (from h in db.NotenAenderungen.AsNoTracking()
                           join n in db.Noten on h.NoteId equals n.Id
                           join u in db.Users on h.GeaendertVonId equals u.Id
                           where n.BewertungId == bewertungId && n.EinschreibungId == einschreibungId
                           orderby h.ZeitUtc descending, h.ErstelltAmUtc descending
                           select new { h.ZeitUtc, u.Vorname, u.Nachname, h.AlterStatus, h.AltePunkte, h.NeuerStatus, h.NeuePunkte }).ToListAsync(ct);
        return liste.Select(h => new NotenHistorie(h.ZeitUtc, $"{h.Vorname} {h.Nachname}".Trim(),
            h.AlterStatus is null ? "—" : Anzeige(h.AlterStatus.Value, h.AltePunkte), Anzeige(h.NeuerStatus, h.NeuePunkte))).ToList();
    }

    private static string Anzeige(NotenStatus status, decimal? punkte) => status == NotenStatus.Leer ? "(retirée)" : NotenTexte.Note(status, punkte);
}
