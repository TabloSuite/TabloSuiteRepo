using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Finanzen;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Medien;
using Skoulou.WebApp.Services.Organisation;
using Skoulou.WebApp.Services.Organisation.Druck;

namespace Skoulou.WebApp.Services.Finanzen;

public sealed record KanalSumme(string Kanal, int Anzahl, long Betrag);
public sealed record RueckstandZeile(string Name, string? Klasse, long Offen, long EnRetard);

/// <summary>Finanzbericht eines Zeitraums: Kasse nach Kategorie, Zahlungen nach Kanal, Forderungen der Familien.</summary>
public sealed record BerichtDaten(
    SchulDokument Schule, DateOnly Von, DateOnly Bis, long SaldoAnfang, IReadOnlyList<KategorieSumme> Recettes, IReadOnlyList<KategorieSumme> Depenses,
    long TotalRecettes, long TotalDepenses, long SaldoEnde, IReadOnlyList<KanalSumme> Kanaele, int Stornos, long StornoBetrag,
    string? Schuljahr, long Forderungen, long EnRetard, int SchuelerEnRetard, IReadOnlyList<RueckstandZeile> Rueckstaende, DateOnly Erstellt);

/// <summary>Rapport financier (Bildschirm und PDF) – nur Direktion mit <see cref="Faehigkeit.FinanzenEinsehen"/>.</summary>
public sealed class BerichtService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, IMedienSpeicher speicher, TimeProvider zeit)
{
    private DateOnly Heute => new SchulZeit(zeit).Heute;

    /// <summary>Vorschlag: laufendes Schuljahr bis heute.</summary>
    public async Task<(DateOnly Von, DateOnly Bis)> StandardZeitraumAsync(Guid akteurId, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, false, ct, Faehigkeit.FinanzenEinsehen);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var jahr = await SchulAbfragen.LaufendesSchuljahrAsync(db, a.SchuleId, ct);
        var heute = Heute;
        return jahr is null ? (new DateOnly(heute.Year, 1, 1), heute) : (jahr.Beginn, jahr.Ende < heute ? jahr.Ende : heute);
    }

    public async Task<BerichtDaten> BerichtAsync(Guid akteurId, DateOnly von, DateOnly bis, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, false, ct, Faehigkeit.FinanzenEinsehen);
        if (bis < von) throw new FachFehler("La date de fin doit suivre la date de début.", "bis");
        if (bis.DayNumber - von.DayNumber > 3 * 366) throw new FachFehler("Choisissez une période de trois ans au plus.", "von");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var alle = db.Buchungen.Where(b => b.SchuleId == a.SchuleId);
        var saldoAnfang = await JournalService.SaldoAsync(alle.Where(b => b.Datum < von), ct);
        var buchungen = await (from b in alle
                               where b.Datum >= von && b.Datum <= bis
                               join k in db.Kategorien on b.KategorieId equals k.Id
                               group b by new { k.Id, k.Name, k.Richtung, BRichtung = b.Richtung } into g
                               select new { g.Key.Id, g.Key.Name, g.Key.Richtung, g.Key.BRichtung, Summe = g.Sum(x => x.Betrag) }).ToListAsync(ct);
        var summen = Kasse.NachKategorie(buchungen.Select(b => new KategorieBuchung(b.Id, b.Name, b.Richtung, b.BRichtung, b.Summe)));
        var recettes = summen.Where(s => s.Richtung == BuchungsRichtung.Recette).ToList();
        var depenses = summen.Where(s => s.Richtung == BuchungsRichtung.Depense).ToList();
        var bewegung = buchungen.Sum(b => Kasse.MitVorzeichen(b.BRichtung, b.Summe));

        var zahlungen = await db.Zahlungen.Where(z => z.SchuleId == a.SchuleId && z.Datum >= von && z.Datum <= bis)
            .GroupBy(z => new { z.Kanal, Storniert = z.StorniertAmUtc != null })
            .Select(g => new { g.Key.Kanal, g.Key.Storniert, Anzahl = g.Count(), Summe = g.Sum(z => z.Betrag) }).ToListAsync(ct);
        var kanaele = zahlungen.Where(z => !z.Storniert).OrderBy(z => z.Kanal)
            .Select(z => new KanalSumme(FinanzEingabe.KanalText(z.Kanal), z.Anzahl, z.Summe)).ToList();

        // Forderungen der Familien im Schuljahr, das das Ende des Zeitraums enthält (sonst laufendes Jahr).
        var jahr = await db.Schuljahre.AsNoTracking().Where(j => j.SchuleId == a.SchuleId && j.Beginn <= bis && j.Ende >= bis).FirstOrDefaultAsync(ct)
                   ?? await SchulAbfragen.LaufendesSchuljahrAsync(db, a.SchuleId, ct);
        long forderungen = 0, enRetard = 0;
        var anzahlRetard = 0;
        List<RueckstandZeile> rueckstaende = [];
        if (jahr is not null)
        {
            var ids = await KontoLader.SchuelerDesJahresAsync(db, jahr.Id, null, ct);
            var konten = await KontoLader.KontenAsync(db, a.SchuleId, jahr.Id, ids, Heute, ct);
            forderungen = konten.Values.Sum(k => k.Offen);
            enRetard = konten.Values.Sum(k => k.EnRetardBetrag);
            anzahlRetard = konten.Values.Count(k => k.IstEnRetard);
            var top = konten.Where(k => k.Value.IstEnRetard).OrderByDescending(k => k.Value.EnRetardBetrag).Take(15).ToList();
            var koepfe = await KontoLader.KoepfeAsync(db, jahr.Id, top.Select(t => t.Key).ToList(), ct);
            rueckstaende = top.Select(t => new RueckstandZeile(koepfe[t.Key].Name, koepfe[t.Key].Klasse, t.Value.Offen, t.Value.EnRetardBetrag)).ToList();
        }

        return new BerichtDaten(
            await QuittungService.SchulDokumentAsync(db, speicher, a.SchuleId, ct), von, bis, saldoAnfang, recettes, depenses,
            recettes.Sum(r => r.Betrag), depenses.Sum(d => d.Betrag), saldoAnfang + bewegung, kanaele,
            zahlungen.Where(z => z.Storniert).Sum(z => z.Anzahl), zahlungen.Where(z => z.Storniert).Sum(z => z.Summe),
            jahr?.Bezeichnung, forderungen, enRetard, anzahlRetard, rueckstaende, Heute);
    }

    public async Task<PdfDatei> PdfAsync(Guid akteurId, DateOnly von, DateOnly bis, CancellationToken ct = default)
    {
        var daten = await BerichtAsync(akteurId, von, bis, ct);
        return new PdfDatei(BerichtPdf.Erzeuge(daten), $"rapport-financier-{von:yyyyMMdd}-{bis:yyyyMMdd}.pdf");
    }
}
