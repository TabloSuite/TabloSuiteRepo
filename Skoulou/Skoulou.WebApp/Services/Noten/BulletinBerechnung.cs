using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Noten;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Noten;

/// <summary>Schüler der Kohorte (aktiv in der Klasse am Periodenende).</summary>
public sealed record KohortenSchueler(Guid EinschreibungId, Guid SchuelerId, string Vorname, string Nachname, Geschlecht Geschlecht, DateOnly? Geburtsdatum,
    Guid? FotoMediumId, string? Matrikel, string? Geburtsort, string? Nationalitaet, bool Interne, bool Affecte, bool Wiederholer)
{
    public string Name => NotenFormat.Name(Vorname, Nachname);
}

public sealed record FehlendeNote(Guid EinschreibungId, string Schueler, string Fach, string Bewertung, DateOnly? Datum);

internal sealed record FachInfo(Guid FachId, Guid KlassenfachId, string Name, FachGruppe Gruppe, int Reihenfolge, decimal Koeffizient, string? Lehrer);

/// <summary>
/// Lädt alles für eine Klasse und Periode einmal (Fachkonzept 04 §7.4) und rechnet mit den Core-Rechnern: Ergebnisse der
/// Periode und der früheren zählenden Perioden, Bilan in der letzten Periode, fehlende Noten, Conseil, Anwesenheit, Vorlage.
/// Klassenwechsel (Bauplan §11.6): Kohorte = Klasse am Periodenende; Noten aus früheren Klassen desselben Fachs zählen mit.
/// </summary>
internal sealed class BulletinBerechnung
{
    public required Schule Schule { get; init; }
    public required Schuljahr Jahr { get; init; }
    public required Klasse Klasse { get; init; }
    public required Periode Periode { get; init; }
    public required IReadOnlyList<Periode> Perioden { get; init; }
    public required IReadOnlyList<FachInfo> Faecher { get; init; }
    public required IReadOnlyList<KohortenSchueler> Kohorte { get; init; }
    public required IReadOnlyDictionary<Guid, KlassenErgebnis> Ergebnisse { get; init; }
    public required IReadOnlyDictionary<Guid, JahresErgebnis>? Bilan { get; init; }
    public required bool IstLetztePeriode { get; init; }
    public required IReadOnlyList<FehlendeNote> Fehlend { get; init; }
    public required IReadOnlyDictionary<(Guid Schueler, Guid Fach), string> Teilnoten { get; init; }
    public required IReadOnlyDictionary<(Guid Schueler, Guid Fach), string> Bemerkungen { get; init; }
    public required IReadOnlyDictionary<Guid, KlassenratEintrag> Conseil { get; init; }
    public required IReadOnlyDictionary<Guid, Jahresentscheidung> Entscheidungen { get; init; }
    public required IReadOnlyDictionary<Guid, (AnwesenheitsSumme Summe, bool Erfasst)> Anwesenheit { get; init; }
    public required BulletinVorlage Vorlage { get; init; }
    public required string? Klassenlehrer { get; init; }

    public KlassenErgebnis Ergebnis => Ergebnisse[Periode.Id];

    public SchuelerErgebnis ErgebnisVon(Guid schuelerId) => Ergebnis.Fuer(schuelerId)!;

    public static async Task<BulletinBerechnung> LadeAsync(SkoulouDbContext db, Klasse klasse, Periode periode, CancellationToken ct)
    {
        var schule = await db.Schulen.AsNoTracking().FirstAsync(s => s.Id == klasse.SchuleId, ct);
        var jahr = await db.Schuljahre.AsNoTracking().FirstAsync(j => j.Id == klasse.SchuljahrId, ct);
        var perioden = await db.Perioden.AsNoTracking().Where(p => p.SchuljahrId == jahr.Id).OrderBy(p => p.Beginn).ThenBy(p => p.Nr).ToListAsync(ct);
        var istLetzte = periode.ZaehltFuerJahr && NotenRechner.IstLetztePeriode(perioden.Select(p => (p.Id, p.Ende, p.ZaehltFuerJahr)), periode.Id);
        var relevant = periode.ZaehltFuerJahr
            ? perioden.Where(p => p.ZaehltFuerJahr && p.Ende <= periode.Ende).ToList()
            : [periode];

        var faecher = await FaecherAsync(db, klasse.Id, ct);
        var kohorte = await KohorteAsync(db, klasse, periode, ct);
        var schuelerIds = kohorte.Select(k => k.SchuelerId).ToList();
        var einschreibungen = await SchulAbfragen.GueltigeEinschreibungen(db).AsNoTracking()
            .Where(e => e.SchuljahrId == jahr.Id && schuelerIds.Contains(e.SchuelerId))
            .Select(e => new { e.Id, e.SchuelerId, e.KlasseId, e.Von, e.Bis }).ToListAsync(ct);
        var einschreibungIds = einschreibungen.Select(e => e.Id).ToList();
        var klassenIds = einschreibungen.Select(e => e.KlasseId).Distinct().ToList();
        var fachIds = faecher.Select(f => f.FachId).ToList();
        var periodenIds = relevant.Select(p => p.Id).ToList();

        var bewertungen = await (from b in db.Bewertungen.AsNoTracking()
                                 join kf in db.Klassenfaecher on b.KlassenfachId equals kf.Id
                                 where periodenIds.Contains(b.PeriodeId) && klassenIds.Contains(kf.KlasseId) && fachIds.Contains(kf.FachId)
                                 orderby b.Datum, b.ErstelltAmUtc
                                 select new { b.Id, b.PeriodeId, kf.KlasseId, kf.FachId, b.Titel, b.Datum, b.Bareme, b.Gewicht }).ToListAsync(ct);
        var bewIds = bewertungen.Select(b => b.Id).ToList();
        var noten = (await db.Noten.AsNoTracking().Where(n => bewIds.Contains(n.BewertungId) && einschreibungIds.Contains(n.EinschreibungId))
                .Select(n => new { n.BewertungId, n.EinschreibungId, n.Status, n.Punkte }).ToListAsync(ct))
            .ToDictionary(n => (n.BewertungId, n.EinschreibungId));

        var ergebnisse = new Dictionary<Guid, KlassenErgebnis>();
        var fehlend = new List<FehlendeNote>();
        var teilnoten = new Dictionary<(Guid, Guid), string>();
        var spalten = faecher.Select(f => new FachSpalte(f.FachId, f.Koeffizient)).ToList();
        foreach (var p in relevant)
        {
            var werte = new List<SchuelerWertungen>();
            foreach (var s in kohorte)
            {
                var eigene = einschreibungen.Where(e => e.SchuelerId == s.SchuelerId).ToList();
                var jeFach = new Dictionary<Guid, IReadOnlyList<Wertung>>();
                foreach (var f in faecher)
                {
                    var liste = new List<Wertung>();
                    var texte = new List<string>();
                    foreach (var b in bewertungen.Where(b => b.PeriodeId == p.Id && b.FachId == f.FachId))
                    {
                        var inKlasse = eigene.Where(e => e.KlasseId == b.KlasseId).ToList();
                        var note = inKlasse.Select(e => noten.GetValueOrDefault((b.Id, e.Id))).FirstOrDefault(n => n is not null && n.Status != NotenStatus.Leer);
                        if (note is not null)
                        {
                            liste.Add(new Wertung(b.Bareme, b.Gewicht, note.Status, note.Punkte));
                            texte.Add(note.Status == NotenStatus.Notee && note.Punkte is { } pk
                                ? NotenTexte.Punkte(NotenRechner.Runde(NotenRechner.AufZwanzig(pk, b.Bareme)))
                                : NotenTexte.StatusKuerzel(note.Status));
                            continue;
                        }
                        var erwartet = inKlasse.Any(e => b.Datum is { } d
                            ? d >= e.Von && (e.Bis is null || d <= e.Bis)
                            : e.Id == s.EinschreibungId && e.Von <= p.Ende && (e.Bis is null || e.Bis >= p.Beginn));
                        if (!erwartet) continue;
                        liste.Add(new Wertung(b.Bareme, b.Gewicht, null, null));
                        if (p.Id == periode.Id) fehlend.Add(new FehlendeNote(s.EinschreibungId, s.Name, f.Name, b.Titel, b.Datum));
                    }
                    if (liste.Count > 0) jeFach[f.FachId] = liste;
                    if (p.Id == periode.Id && texte.Count > 0) teilnoten[(s.SchuelerId, f.FachId)] = string.Join(" · ", texte);
                }
                werte.Add(new SchuelerWertungen(s.SchuelerId, jeFach));
            }
            ergebnisse[p.Id] = KlassenRechner.Berechne(spalten, werte, schule.AbsenzOhneEntschuldigungZaehltNull);
        }

        var bilan = istLetzte
            ? KlassenRechner.Bilan(schuelerIds, fachIds, relevant.Select(p => (ergebnisse[p.Id], p.Jahresgewicht, p.ZaehltFuerJahr)).ToList())
            : null;

        var kohortenIds = kohorte.Select(k => k.EinschreibungId).ToList();
        var conseil = await db.KlassenratEintraege.AsNoTracking().Where(k => k.PeriodeId == periode.Id && kohortenIds.Contains(k.EinschreibungId))
            .ToDictionaryAsync(k => k.EinschreibungId, ct);
        var entscheidungen = await db.Jahresentscheidungen.AsNoTracking().Where(j => kohortenIds.Contains(j.EinschreibungId))
            .ToDictionaryAsync(j => j.EinschreibungId, ct);
        var fachVonKf = await db.Klassenfaecher.AsNoTracking().Where(kf => klassenIds.Contains(kf.KlasseId)).ToDictionaryAsync(kf => kf.Id, kf => kf.FachId, ct);
        var bemerkungen = new Dictionary<(Guid, Guid), string>();
        foreach (var b in await db.FachBemerkungen.AsNoTracking().Where(b => b.PeriodeId == periode.Id && einschreibungIds.Contains(b.EinschreibungId))
                     .OrderBy(b => b.GeaendertAmUtc).ToListAsync(ct))
        {
            var schueler = einschreibungen.First(e => e.Id == b.EinschreibungId).SchuelerId;
            if (fachVonKf.TryGetValue(b.KlassenfachId, out var fachId)) bemerkungen[(schueler, fachId)] = b.Text;
        }
        var anwesenheit = (await db.Anwesenheiten.AsNoTracking()
                .Where(x => einschreibungIds.Contains(x.EinschreibungId) && x.Datum >= periode.Beginn && x.Datum <= periode.Ende)
                .Select(x => new { x.EinschreibungId, x.Status, x.Entschuldigt, x.Minuten }).ToListAsync(ct))
            .GroupBy(x => einschreibungen.First(e => e.Id == x.EinschreibungId).SchuelerId)
            .ToDictionary(g => g.Key, g => (AnwesenheitService.Summe(g.Select(x => (x.Status, x.Entschuldigt, x.Minuten))), true));
        var vorlage = await db.BulletinVorlagen.AsNoTracking().Where(v => v.SchuleId == schule.Id && (v.StufeId == klasse.StufeId || v.StufeId == null))
            .OrderBy(v => v.StufeId == null).FirstOrDefaultAsync(ct) ?? new BulletinVorlage { SchuleId = schule.Id };
        var klassenlehrer = klasse.KlassenlehrerId is { } kl
            ? await db.Users.Where(u => u.Id == kl).Select(u => u.Vorname + " " + u.Nachname).FirstOrDefaultAsync(ct)
            : null;

        return new BulletinBerechnung
        {
            Schule = schule, Jahr = jahr, Klasse = klasse, Periode = periode, Perioden = perioden, Faecher = faecher, Kohorte = kohorte,
            Ergebnisse = ergebnisse, Bilan = bilan, IstLetztePeriode = istLetzte, Fehlend = fehlend, Teilnoten = teilnoten, Bemerkungen = bemerkungen,
            Conseil = conseil, Entscheidungen = entscheidungen, Anwesenheit = anwesenheit, Vorlage = vorlage, Klassenlehrer = klassenlehrer
        };
    }

    private static async Task<List<FachInfo>> FaecherAsync(SkoulouDbContext db, Guid klasseId, CancellationToken ct)
    {
        var faecher = await (from kf in db.Klassenfaecher.AsNoTracking()
                             join f in db.Faecher on kf.FachId equals f.Id
                             where kf.KlasseId == klasseId
                             select new { kf.Id, kf.FachId, f.Name, f.Gruppe, Reihenfolge = kf.Reihenfolge ?? f.Reihenfolge, kf.Koeffizient }).ToListAsync(ct);
        var ids = faecher.Select(f => f.Id).ToList();
        var lehrer = await (from l in db.Lehrauftraege.AsNoTracking()
                            join u in db.Users on l.LehrerId equals u.Id
                            where ids.Contains(l.KlassenfachId)
                            select new { l.KlassenfachId, Name = u.Vorname + " " + u.Nachname }).ToListAsync(ct);
        return faecher.OrderBy(f => f.Reihenfolge).ThenBy(f => f.Name)
            .Select(f => new FachInfo(f.FachId, f.Id, f.Name, f.Gruppe, f.Reihenfolge, f.Koeffizient,
                string.Join(", ", lehrer.Where(l => l.KlassenfachId == f.Id).Select(l => l.Name.Trim()).Distinct()) is { Length: > 0 } t ? t : null))
            .ToList();
    }

    /// <summary>Kohorte = gültige Einschreibungen der Klasse am Periodenende (Effectif, Rang; Bauplan §5 Regel 5).</summary>
    internal static async Task<List<KohortenSchueler>> KohorteAsync(SkoulouDbContext db, Klasse klasse, Periode periode, CancellationToken ct)
    {
        var liste = await (from e in SchulAbfragen.GueltigeEinschreibungen(db).AsNoTracking()
                           join u in db.Users on e.SchuelerId equals u.Id
                           join p in db.SchuelerProfile on u.Id equals p.KontoId into profile
                           from p in profile.DefaultIfEmpty()
                           where e.KlasseId == klasse.Id && e.Von <= periode.Ende && (e.Bis == null || e.Bis >= periode.Ende)
                           select new KohortenSchueler(e.Id, u.Id, u.Vorname, u.Nachname, u.Geschlecht, u.Geburtsdatum, u.FotoMediumId,
                               p == null ? null : p.Matrikel, p == null ? null : p.Geburtsort, p == null ? null : p.Nationalitaet,
                               p != null && p.IstInterne, p != null && p.IstAffecte, e.IstWiederholer)).ToListAsync(ct);
        return liste.OrderBy(s => s.Name, StringComparer.Create(SkoulouKultur.Kultur, true)).ThenBy(s => s.Matrikel).ToList();
    }

    /// <summary>Distinktion laut Conseil oder Vorschlag aus der Moyenne.</summary>
    public Distinktion DistinktionVon(KohortenSchueler s)
        => Conseil.TryGetValue(s.EinschreibungId, out var c) && c.Distinktion is { } d ? d : NotenTexte.DistinktionVorschlag(ErgebnisVon(s.SchuelerId).Moyenne.Wert);

    /// <summary>Inhalt des Bulletins eines Schülers der Kohorte.</summary>
    public BulletinInhalt Inhalt(KohortenSchueler s)
    {
        var e = ErgebnisVon(s.SchuelerId);
        var layout = SnapshotLayout.Aus(Vorlage);
        var bilan = Bilan?.GetValueOrDefault(s.SchuelerId);
        var faecher = Faecher.Select(f =>
        {
            var fe = e.Faecher[f.FachId];
            var wert = fe.Mittel.Gerundet;
            var bemerkung = Bemerkungen.GetValueOrDefault((s.SchuelerId, f.FachId)) ?? (wert is { } w ? NotenTexte.Appreciation(w) : null);
            return new SnapshotFach(f.Name, Gruppe(f.Gruppe), f.Lehrer, f.Koeffizient, fe.Mittel.Wert, NotenTexte.MittelKurz(fe.Mittel),
                fe.Mittel.Wert is { } m ? m * f.Koeffizient : null, fe.Mittel.HatWert ? fe.Rang.Text : null,
                Teilnoten.GetValueOrDefault((s.SchuelerId, f.FachId)), bemerkung,
                bilan is null ? null : NotenTexte.MittelKurz(bilan.JeFach.GetValueOrDefault(f.FachId)));
        }).ToList();
        var mitWert = Faecher.Where(f => e.Faecher[f.FachId].Mittel.HatWert).ToList();
        var gruppen = Faecher.Where(f => f.Gruppe != FachGruppe.Aucune).GroupBy(f => f.Gruppe).OrderBy(g => g.Key)
            .Select(g => new SnapshotGruppe(Gruppe(g.Key)!, NotenTexte.MittelKurz(KlassenRechner.GruppenMittel(g.Select(f => (e.Faecher[f.FachId].Mittel, f.Koeffizient)))),
                g.Sum(f => f.Koeffizient)))
            .ToList();
        var vorperioden = Perioden.Where(p => p.ZaehltFuerJahr && p.Ende < Periode.Ende && Ergebnisse.ContainsKey(p.Id))
            .Select(p => Ergebnisse[p.Id].Fuer(s.SchuelerId)!)
            .Zip(Perioden.Where(p => p.ZaehltFuerJahr && p.Ende < Periode.Ende && Ergebnisse.ContainsKey(p.Id)),
                (r, p) => new SnapshotPeriode(p.Titel, NotenTexte.MittelKurz(r.Moyenne), r.Rang.Text))
            .ToList();
        var stat = Ergebnis.Statistik;
        var distinktion = DistinktionVon(s);
        Conseil.TryGetValue(s.EinschreibungId, out var conseil);
        var (summe, erfasst) = Anwesenheit.GetValueOrDefault(s.SchuelerId, (AnwesenheitsSumme.Leer, false));
        var entscheidung = Entscheidungen.TryGetValue(s.EinschreibungId, out var je) ? je.Entscheidung : JahresEntscheidungsArt.Offen;
        return new BulletinInhalt(
            SchulKopf(Schule), Jahr.Bezeichnung, Periode.Titel, NotenTexte.BulletinTitel(Jahr.PeriodenArt, Periode.ZaehltFuerJahr), Klasse.Name, Kohorte.Count,
            new SnapshotSchueler(s.Nachname, s.Vorname, s.Matrikel, s.Geburtsdatum is { } g ? SkoulouKultur.Datum(g) : null, s.Geburtsort,
                s.Geschlecht switch { Geschlecht.Maennlich => "M", Geschlecht.Weiblich => "F", _ => null }, s.Nationalitaet, s.Interne, s.Affecte, s.Wiederholer),
            faecher, gruppen, e.Moyenne.Wert, NotenTexte.Mittel(e.Moyenne), e.Moyenne.Gerundet is { } mg ? NoteInWorten.Text(mg) : null, e.Rang.Text,
            mitWert.Sum(f => f.Koeffizient), mitWert.Count == 0 ? null : mitWert.Sum(f => e.Faecher[f.FachId].Mittel.Wert!.Value * f.Koeffizient),
            e.Moyenne.Gerundet is { } ma ? NotenTexte.Appreciation(ma) : null,
            stat.Klassiert == 0 ? null : new SnapshotStatistik(NotenTexte.Zahl(stat.Mittel!.Value), NotenTexte.Zahl(stat.Min!.Value), NotenTexte.Zahl(stat.Max!.Value),
                NotenTexte.Zahl(stat.ProzentUeber10!.Value), stat.Klassiert),
            vorperioden,
            bilan is null ? null : new SnapshotBilan(NotenTexte.Mittel(bilan.Moyenne), bilan.Rang.Text, NotenTexte.Entscheidung(entscheidung),
                [.. vorperioden, new SnapshotPeriode(Periode.Titel, NotenTexte.MittelKurz(e.Moyenne), e.Rang.Text)]),
            distinktion, NotenTexte.DistinktionText(distinktion), conseil?.Sanktionen ?? Sanktionen.Keine,
            NotenTexte.SanktionenTexte(conseil?.Sanktionen ?? Sanktionen.Keine), conseil?.Bemerkung,
            new SnapshotAnwesenheit(erfasst, summe.Absences, summe.AbsencesJustifiees, summe.Retards, summe.RetardMinuten),
            Klassenlehrer, layout);
    }

    /// <summary>Inhalte und Hashes aller Schüler der Kohorte (in fester Reihenfolge) und der Klassen-Hash.</summary>
    public (IReadOnlyList<(KohortenSchueler Schueler, BulletinInhalt Inhalt, string Hash)> Liste, string KlassenHash) AlleInhalte()
    {
        var liste = Kohorte.Select(s => { var i = Inhalt(s); return (s, i, BulletinSnapshot.HashVon(i)); }).ToList();
        return (liste, BulletinSnapshot.HashVon(liste.Select(x => x.Item3)));
    }

    public static SnapshotSchule SchulKopf(Schule s)
        => new(s.OffizielleBezeichnung ?? s.Name, s.Slogan, s.Adresse,
            string.Join(" · ", new[] { s.Telefon, s.Email }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } k ? k : null,
            s.Zulassungsnummer, s.Ministerium, s.RepublikZeile, s.StaatsMotto, s.DirectionRegionale, s.Statut, s.Ort);

    public static string? Gruppe(FachGruppe g) => g switch
    {
        FachGruppe.Lettres => "Lettres",
        FachGruppe.Sciences => "Sciences",
        FachGruppe.Autres => "Autres",
        _ => null
    };
}
