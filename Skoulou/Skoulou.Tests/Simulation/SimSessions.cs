using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Fehler;

namespace Skoulou.Tests.Simulation;

/// <summary>S3: Invarianten der Sprachschulen (Gebühr je Session, Session-Ende).</summary>
internal sealed partial class Simulation
{
    /// <summary>
    /// Posten einer Session nur für Teilnehmer dieser Session: für jedes Schülerkonto der Sprachschulen mit Session-Posten
    /// (alle Jahre) sind die Session-Posten im Konto der App genau die aus den Einschreibungen der DB unabhängig
    /// nachgerechneten (effektiver Zeitraum = Einschreibung ∩ Session der Klasse) und genau die des Gebührenbuchs der Simulation.
    /// </summary>
    private async Task SessionPostenAsync()
    {
        var daten = await d.MitDbAsync(async db => new
        {
            Posten = await db.GebuehrenPosten.Where(p => p.PeriodeId != null)
                .Select(p => new { p.Id, p.SchuleId, p.SchuljahrId, PeriodeId = p.PeriodeId!.Value, p.KlasseId, p.StufeId }).ToListAsync(),
            Perioden = await db.Perioden.Select(p => new { p.Id, p.Beginn, p.Ende }).ToDictionaryAsync(p => p.Id),
            Einschreibungen = await (from e in db.Einschreibungen
                                     where e.StorniertAmUtc == null
                                     join k in db.Klassen on e.KlasseId equals k.Id
                                     select new { e.SchuelerId, e.SchuljahrId, e.KlasseId, k.StufeId, k.PeriodeId, e.Von, e.Bis }).ToListAsync()
        });
        int konten = 0, zuordnungen = 0, jahre = 0;
        foreach (var jahr in daten.Posten.GroupBy(p => p.SchuljahrId))
        {
            var schule = Schulen.First(s => s.Id == jahr.First().SchuleId);
            var simJahr = schule.Jahre.FirstOrDefault(j => j.Id == jahr.Key);
            var posten = jahr.ToList();
            var ergebnisse = new ConcurrentBag<(Guid Schueler, HashSet<Guid> Ist, HashSet<Guid> Soll, HashSet<Guid>? Buch)>();
            var gruppen = daten.Einschreibungen.Where(e => e.SchuljahrId == jahr.Key).GroupBy(e => e.SchuelerId).ToList();
            await System.Threading.Tasks.Parallel.ForEachAsync(gruppen, new ParallelOptions { MaxDegreeOfParallelism = Parallel }, async (g, _) =>
            {
                var soll = posten.Where(p => g.Any(e =>
                {
                    if (p.KlasseId is { } kl ? kl != e.KlasseId : p.StufeId is { } st && st != e.StufeId) return false;
                    var von = e.Von;
                    var bis = e.Bis;
                    if (e.PeriodeId is { } kp)
                    {
                        var ks = daten.Perioden[kp];
                        if (ks.Beginn > von) von = ks.Beginn;
                        if (bis is null || ks.Ende < bis) bis = ks.Ende;
                    }
                    var session = daten.Perioden[p.PeriodeId];
                    return (bis is null || bis >= von) && von <= session.Ende && (bis is null || bis >= session.Beginn);
                })).Select(p => p.Id).ToHashSet();
                HashSet<Guid>? buch = null;
                if (simJahr is not null && schule.Schueler.TryGetValue(g.Key, out var x) && x.Konten.TryGetValue(jahr.Key, out var k))
                    buch = simJahr.Posten.Where(p => p.PeriodeId is not null && simJahr.Gilt(p, k)).Select(p => p.Id).ToHashSet();
                try
                {
                    var konto = await api.Gebuehren.KontoAsync(schule.Direktor, jahr.Key, g.Key);
                    var ist = konto.Konto.Zeilen.Where(z => z.PostenId is { } pid && posten.Any(p => p.Id == pid)).Select(z => z.PostenId!.Value).ToHashSet();
                    ergebnisse.Add((g.Key, ist, soll, buch));
                }
                catch (Exception ex) when (ex is FachFehler or KeinZugriffFehler)
                {
                    Pruefe(false, $"{schule.P.Name}: Konto von {g.Key} nicht lesbar ({ex.Message}).");
                }
            });
            foreach (var (schueler, ist, soll, buch) in ergebnisse)
            {
                Pruefe(ist.SetEquals(soll), $"{schule.P.Name} {simJahr?.Bezeichnung}: Schüler {schueler} hat {ist.Count} Session-Posten, nachgerechnet {soll.Count} ({ist.Except(soll).Count()} ohne Teilnahme).");
                Pruefe(buch is null || buch.SetEquals(soll), $"{schule.P.Name} {simJahr?.Bezeichnung}: Gebührenbuch der Simulation für {schueler}: {buch?.Count} Session-Posten, nachgerechnet {soll.Count}.");
                zuordnungen += ist.Count;
            }
            konten += ergebnisse.Count;
            jahre++;
        }
        Geprueft.Add($"Session-Posten nur für Teilnehmer: {konten} Schülerkonten in {jahre} Schuljahren, {zuordnungen} Session-Posten = unabhängig aus den Einschreibungen = Gebührenbuch der Simulation");
    }

    /// <summary>
    /// Kein Schüler nach Session-Ende in Abrechnung und Gebühren: vergessene Teilnehmer (weder Wechsel noch Austritt) haben
    /// für die Sessions nach ihrer – bis zu einer Rückkehr – weder eine gültige Rechnungsposition noch einen Session-Posten.
    /// (Die Listen prüft die Simulation an jedem Session-Wechsel, siehe <c>NachSessionEndePruefenAsync</c>.)
    /// </summary>
    private async Task SessionEndeAsync()
    {
        var vergessene = Schulen.SelectMany(s => s.Vergessene.Select(v => (S: s, V: v))).ToList();
        var ids = vergessene.Select(x => x.V.Schueler.Id).Distinct().ToList();
        var daten = await d.MitDbAsync(async db => new
        {
            Positionen = await (from p in db.Rechnungspositionen
                                where ids.Contains(p.SchuelerId) && p.StorniertGrund == null
                                join per in db.Perioden on p.PeriodeId equals per.Id
                                select new { p.SchuelerId, per.SchuljahrId, per.Nr }).ToListAsync(),
            Posten = await (from p in db.GebuehrenPosten
                            join per in db.Perioden on p.PeriodeId equals per.Id
                            select new { p.Id, per.SchuljahrId, per.Nr }).ToListAsync()
        });
        var konten = 0;
        foreach (var (s, v) in vergessene)
        {
            bool Danach(int nr) => nr > v.Session && (v.ZurueckAb is not { } z || nr < z);
            var positionen = daten.Positionen.Count(p => p.SchuelerId == v.Schueler.Id && p.SchuljahrId == v.JahrId && Danach(p.Nr));
            Pruefe(positionen == 0, $"{s.P.Name}: {v.Schueler.Nachname} {v.Schueler.Vorname} nach Session {v.Session} noch {positionen} Rechnungsposition(en).");
            var spaeter = daten.Posten.Where(p => p.SchuljahrId == v.JahrId && Danach(p.Nr)).Select(p => p.Id).ToHashSet();
            if (spaeter.Count == 0) continue;
            var konto = await api.Gebuehren.KontoAsync(s.Direktor, v.JahrId, v.Schueler.Id);
            var falsch = konto.Konto.Zeilen.Count(z => z.PostenId is { } pid && spaeter.Contains(pid));
            Pruefe(falsch == 0, $"{s.P.Name}: {v.Schueler.Nachname} {v.Schueler.Vorname} schuldet nach Session {v.Session} noch {falsch} Rate(n) späterer Sessions.");
            konten++;
        }
        Geprueft.Add($"Session-Ende: {vergessene.Count} vergessene Teilnehmer ({vergessene.Count(x => x.V.ZurueckAb is not null)} zurück, {vergessene.Count(x => x.V.Archiviert)} archiviert) ohne Rechnungsposition danach, {konten} Konten ohne spätere Session-Posten; Listen an {Zaehler.GetValueOrDefault("Listenprüfungen nach Session-Ende")} Session-Wechseln geprüft");
    }
}
