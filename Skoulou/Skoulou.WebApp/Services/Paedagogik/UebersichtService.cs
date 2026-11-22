using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Paedagogik;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>
/// Startseiten und Ergebnisse: Lehrkraft (Klassen aus Lehraufträgen, offene Korrekturen, Fristen) und
/// Schüler (zu erledigen, Ergebnisse). Score überall gleich: punktgewichtet über die gewerteten Versuche
/// (<see cref="ScoreRechner.Gesamt"/>); überfällige, nie begonnene Zuweisungen zählen mit 0 Punkten,
/// Versuche mit offener Korrektur zählen erst nach der Korrektur.
/// </summary>
public sealed class UebersichtService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, TimeProvider zeit)
{
    private DateTime Jetzt => zeit.GetUtcNow().UtcDateTime;

    // ---------- Lehrkraft ----------

    public async Task<LehrerStart> LehrerStartAsync(Guid akteurId, Guid? schuljahrId, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.LeserAsync(zugriff, akteurId, ct);
        var schuleId = akteur.SchuleId!.Value;
        var jetzt = Jetzt;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var jahr = schuljahrId ?? (await SchulAbfragen.LaufendesSchuljahrAsync(db, schuleId, ct))?.Id;

        var auftraege = await (from l in db.Lehrauftraege
                               where l.LehrerId == akteur.Id
                               join kf in db.Klassenfaecher on l.KlassenfachId equals kf.Id
                               join k in db.Klassen on kf.KlasseId equals k.Id
                               where k.SchuljahrId == jahr && k.SchuleId == schuleId
                               join f in db.Faecher on kf.FachId equals f.Id
                               select new { KlasseId = k.Id, Klasse = k.Name, Fach = f.Name, f.Reihenfolge }).ToListAsync(ct);
        var klassenIds = auftraege.Select(a => a.KlasseId).Distinct().ToList();
        var effektiv = await SchulAbfragen.AktiveEinschreibungen(db).Where(e => klassenIds.Contains(e.KlasseId))
            .GroupBy(e => e.KlasseId).Select(g => new { g.Key, Anzahl = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Anzahl, ct);
        var klassen = auftraege.GroupBy(a => new { a.KlasseId, a.Klasse }).OrderBy(g => g.Key.Klasse)
            .Select(g => new LehrerKlasse(g.Key.KlasseId, g.Key.Klasse, g.OrderBy(x => x.Reihenfolge).Select(x => x.Fach).Distinct().ToList(),
                effektiv.GetValueOrDefault(g.Key.KlasseId))).ToList();

        var eigene = db.Zuweisungen.Where(z => z.SchuleId == schuleId && z.LehrerId == akteur.Id);
        var korrigierbar = KorrekturService.MitLehrauftrag(db, akteur, eigene);
        var offen = await db.Antworten.CountAsync(a => a.Punkte == null && a.Korrektur == KorrekturArt.Manuelle
            && db.Versuche.Any(v => v.Id == a.VersuchId && korrigierbar.Any(z => z.Id == v.ZuweisungId)), ct);

        var von = jetzt.AddDays(-2);
        var bis = jetzt.AddDays(21);
        var aktive = SchulAbfragen.AktiveEinschreibungen(db);
        var fristen = await eigene.Where(z => z.Status == ZuweisungsStatus.Active && z.FristUtc != null && z.FristUtc >= von && z.FristUtc <= bis)
            .OrderBy(z => z.FristUtc).Take(8)
            .Select(z => new FristEintrag(z.Id, z.LeconId != null ? InhaltsArt.Lecon : InhaltsArt.Pruefung,
                (z.LeconId != null
                    ? db.Lecons.Where(l => l.Id == z.LeconId).Select(l => l.Titel).FirstOrDefault()
                    : db.Pruefungen.Where(p => p.Id == z.PruefungId).Select(p => p.Titel).FirstOrDefault()) ?? "",
                db.Klassen.Where(k => k.Id == z.KlasseId).Select(k => k.Name).FirstOrDefault() ?? "",
                z.FristUtc!.Value,
                db.LernStaende.Count(s => s.ZuweisungId == z.Id && s.Abgeschlossen),
                aktive.Count(e => e.KlasseId == z.KlasseId)))
            .ToListAsync(ct);

        var lecons = await db.Lecons.CountAsync(l => l.SchuleId == schuleId && l.AutorId == akteur.Id && l.Status != InhaltsStatus.Archive, ct);
        var pruefungen = await db.Pruefungen.CountAsync(p => p.SchuleId == schuleId && p.AutorId == akteur.Id && p.Status != InhaltsStatus.Archive, ct);
        return new LehrerStart(klassen, offen, fristen, lecons, pruefungen);
    }

    // ---------- Schüler ----------

    private sealed record Roh(Guid Id, bool IstLecon, string Titel, string Fach, int FachReihenfolge, DateTime AbUtc, DateTime? FristUtc,
        ZuweisungsStatus Status, bool AktuelleKlasse, decimal Max);

    /// <summary>Ergebniszeilen: Zuweisungen der aktuellen Klasse (begonnen/veröffentlicht) und alle mit eigenen Versuchen.</summary>
    private static async Task<(string? Klasse, List<(Roh Roh, ErgebnisZeile Zeile)> Zeilen)> ZeilenAsync(SkoulouDbContext db, Guid schuelerId,
        Guid schuleId, DateTime jetzt, CancellationToken ct)
    {
        var einschreibung = await SchulAbfragen.AktuelleEinschreibungAsync(db, schuelerId, ct);
        var klasseId = einschreibung?.KlasseId;
        var klasse = klasseId is null ? null : await db.Klassen.Where(k => k.Id == klasseId).Select(k => k.Name).FirstOrDefaultAsync(ct);

        var roh = await (from z in db.Zuweisungen.AsNoTracking()
                         where z.SchuleId == schuleId
                               && ((z.KlasseId == klasseId && z.Status == ZuweisungsStatus.Active && z.AbUtc <= jetzt)
                                   || db.Versuche.Any(v => v.ZuweisungId == z.Id && v.SchuelerId == schuelerId))
                         let fachId = z.LeconId != null
                             ? db.Lecons.Where(l => l.Id == z.LeconId).Select(l => l.FachId).FirstOrDefault()
                             : db.Pruefungen.Where(p => p.Id == z.PruefungId).Select(p => p.FachId).FirstOrDefault()
                         select new Roh(z.Id, z.LeconId != null,
                             (z.LeconId != null
                                 ? db.Lecons.Where(l => l.Id == z.LeconId).Select(l => l.Titel).FirstOrDefault()
                                 : db.Pruefungen.Where(p => p.Id == z.PruefungId).Select(p => p.Titel).FirstOrDefault()) ?? "",
                             db.Faecher.Where(f => f.Id == fachId).Select(f => f.Name).FirstOrDefault() ?? "",
                             db.Faecher.Where(f => f.Id == fachId).Select(f => f.Reihenfolge).FirstOrDefault(),
                             z.AbUtc, z.FristUtc, z.Status, z.KlasseId == klasseId,
                             db.Fragen.Where(f => !f.Archiviert && db.AufgabenBloecke.Any(b => b.Id == f.BlockId && !b.Archiviert
                                     && (z.LeconId != null ? b.LeconId == z.LeconId : b.PruefungId == z.PruefungId)))
                                 .Sum(f => (decimal?)f.Punkte) ?? 0))
            .ToListAsync(ct);
        var ids = roh.Select(r => r.Id).ToList();
        var staende = await db.LernStaende.AsNoTracking().Where(s => s.SchuelerId == schuelerId && ids.Contains(s.ZuweisungId))
            .ToDictionaryAsync(s => s.ZuweisungId, ct);

        var zeilen = roh.Select(r =>
        {
            staende.TryGetValue(r.Id, out var s);
            var fertig = s?.Abgeschlossen ?? false;
            var max = s is { MaxPunkte: > 0 } ? s.MaxPunkte : r.Max;
            var ueberfaellig = !fertig && r.FristUtc is { } f && f <= jetzt;
            var zeile = new ErgebnisZeile(r.Id, r.IstLecon ? InhaltsArt.Lecon : InhaltsArt.Pruefung, r.Titel, r.Fach, r.FristUtc,
                fertig ? s!.BestePunkte : 0, max, fertig && max > 0 ? s!.Prozent : ueberfaellig && max > 0 ? 0 : null,
                fertig, s?.KorrekturOffen ?? false, ueberfaellig, s?.AnzahlVersuche ?? 0, null, s?.LetzteAktivitaetUtc);
            return (r, zeile);
        }).ToList();
        return (klasse, zeilen);
    }

    /// <summary>Zählt die Zeile im Score? (gewertet und korrigiert, oder überfällig ohne Versuch).</summary>
    private static bool Zaehlt(ErgebnisZeile z)
        => z.MaxPunkte > 0 && ((z.Abgeschlossen && !z.KorrekturOffen) || (z.Ueberfaellig && z.AnzahlVersuche == 0));

    public static decimal? Score(IEnumerable<ErgebnisZeile> zeilen)
        => ScoreRechner.Gesamt(zeilen.Where(Zaehlt).Select(z => (z.Abgeschlossen ? z.Punkte : 0m, z.MaxPunkte)));

    public async Task<EleveUebersicht> EleveStartAsync(Guid schuelerId, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchuelerAsync(zugriff, schuelerId, ct);
        var jetzt = Jetzt;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (klasse, zeilen) = await ZeilenAsync(db, akteur.Id, akteur.SchuleId!.Value, jetzt, ct);
        var offen = zeilen.Where(x => x.Roh.AktuelleKlasse && x.Roh.Status == ZuweisungsStatus.Active && !x.Zeile.Abgeschlossen
                                      && (x.Roh.FristUtc is null || x.Roh.FristUtc > jetzt))
            .OrderBy(x => x.Roh.FristUtc is null).ThenBy(x => x.Roh.FristUtc).ThenByDescending(x => x.Roh.AbUtc)
            .Select(x => new AufgabeOffen(x.Roh.Id, x.Zeile.Art, x.Roh.Titel, x.Roh.Fach, x.Roh.FristUtc, x.Zeile.AnzahlVersuche > 0))
            .ToList();
        var stimmeAus = await db.SchuelerProfile.Where(p => p.KontoId == akteur.Id).Select(p => p.StimmeAus).FirstOrDefaultAsync(ct);
        var alle = zeilen.Select(x => x.Zeile).ToList();
        return new EleveUebersicht(klasse, offen, Score(alle), alle.Count(z => z.Abgeschlossen), offen.Count, stimmeAus);
    }

    public async Task<SchuelerErgebnisse> ErgebnisseAsync(Guid schuelerId, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchuelerAsync(zugriff, schuelerId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (_, zeilen) = await ZeilenAsync(db, akteur.Id, akteur.SchuleId!.Value, Jetzt, ct);
        var faecher = zeilen.GroupBy(x => new { x.Roh.Fach, x.Roh.FachReihenfolge }).OrderBy(g => g.Key.FachReihenfolge).ThenBy(g => g.Key.Fach)
            .Select(g => new FachErgebnis(g.Key.Fach, Score(g.Select(x => x.Zeile)), g.Count(x => x.Zeile.Abgeschlossen), g.Count()))
            .ToList();
        var liste = zeilen.Select(x => x.Zeile)
            .OrderByDescending(z => z.LetzteAktivitaetUtc ?? z.FristUtc ?? DateTime.MinValue).ToList();
        return new SchuelerErgebnisse(Score(liste), faecher, liste);
    }
}
