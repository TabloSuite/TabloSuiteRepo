using ENote.Core.DataModels.Kasse;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Besitzer-Finanzansicht: Umsatzübersicht, Verkaufsliste und Kassenabschlüsse
/// eines Salons. Optional für delegierte Kassierer mit Finanzrecht.
/// </summary>
public class FinanzService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public record TagesUmsatz(DateTime Datum, int AnzahlVerkaeufe, decimal Bar, decimal Karte, decimal Gesamt);

    /// <summary>Einnahmen, Ausgaben und Saldo (Solde de caisse) eines Zeitraums.</summary>
    public record FinanzUebersicht(
        decimal EinnahmenBar,
        decimal EinnahmenKarte,
        decimal EinnahmenGesamt,
        int AnzahlVerkaeufe,
        decimal AusgabenGesamt,
        IReadOnlyList<KategorieAusgabe> AusgabenNachKategorie)
    {
        public decimal Saldo => EinnahmenGesamt - AusgabenGesamt;
    }

    public record KategorieAusgabe(string Name, decimal Betrag);

    public record VerkaufEintrag(
        Guid VerkaufId,
        DateTime ZeitUtc,
        string KassiererName,
        ZahlungsArt ZahlungsArt,
        decimal GesamtSumme,
        string PositionenText);

    /// <summary>Tagesumsätze der letzten N Tage für einen Salon.</summary>
    public async Task<List<TagesUmsatz>> GetTagesUmsaetzeAsync(Guid salonId, int tage = 14, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ab = DateTime.UtcNow.Date.AddDays(-tage);

        var verkaeufe = await db.Verkaeufe
            .AsNoTracking()
            .Where(v => v.SalonId == salonId && v.ZeitUtc >= ab)
            .Select(v => new { v.ZeitUtc, v.ZahlungsArt, v.GesamtSumme })
            .ToListAsync(cancellationToken);

        return GruppiereNachTag(verkaeufe.Select(v => (v.ZeitUtc, v.ZahlungsArt, v.GesamtSumme)));
    }

    /// <summary>Tagesumsätze in einem frei wählbaren Zeitraum (für Filter + PDF-Export).</summary>
    public async Task<List<TagesUmsatz>> GetTagesUmsaetzeImZeitraumAsync(
        Guid salonId, DateTime vonUtc, DateTime bisUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var verkaeufe = await db.Verkaeufe
            .AsNoTracking()
            .Where(v => v.SalonId == salonId && v.ZeitUtc >= vonUtc && v.ZeitUtc <= bisUtc)
            .Select(v => new { v.ZeitUtc, v.ZahlungsArt, v.GesamtSumme })
            .ToListAsync(cancellationToken);

        return GruppiereNachTag(verkaeufe.Select(v => (v.ZeitUtc, v.ZahlungsArt, v.GesamtSumme)));
    }

    private static List<TagesUmsatz> GruppiereNachTag(IEnumerable<(DateTime ZeitUtc, ZahlungsArt Art, decimal Summe)> verkaeufe)
        => verkaeufe
            .GroupBy(v => v.ZeitUtc.Date)
            .Select(g => new TagesUmsatz(
                g.Key,
                g.Count(),
                g.Where(v => v.Art == ZahlungsArt.Bar).Sum(v => v.Summe),
                g.Where(v => v.Art == ZahlungsArt.Karte).Sum(v => v.Summe),
                g.Sum(v => v.Summe)))
            .OrderByDescending(t => t.Datum)
            .ToList();

    /// <summary>
    /// Finanzübersicht eines Zeitraums: Einnahmen aus Verkäufen, Ausgaben aus der
    /// Ausgabenverwaltung und der daraus resultierende Kassenstand (Saldo).
    /// </summary>
    public async Task<FinanzUebersicht> GetFinanzUebersichtAsync(
        Guid salonId, DateTime vonUtc, DateTime bisUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var verkaeufe = await db.Verkaeufe
            .AsNoTracking()
            .Where(v => v.SalonId == salonId && v.ZeitUtc >= vonUtc && v.ZeitUtc <= bisUtc)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Bar = g.Where(v => v.ZahlungsArt == ZahlungsArt.Bar).Sum(v => v.GesamtSumme),
                Karte = g.Where(v => v.ZahlungsArt != ZahlungsArt.Bar).Sum(v => v.GesamtSumme),
                Anzahl = g.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        var ausgabenRoh = await db.Ausgaben
            .AsNoTracking()
            .Where(a => a.SalonId == salonId && a.DatumUtc >= vonUtc && a.DatumUtc <= bisUtc)
            .Select(a => new { a.Kategorie.Name, a.Betrag })
            .ToListAsync(cancellationToken);

        var ausgaben = ausgabenRoh
            .GroupBy(x => x.Name)
            .Select(g => new KategorieAusgabe(g.Key, g.Sum(x => x.Betrag)))
            .OrderByDescending(x => x.Betrag)
            .ToList();

        return new FinanzUebersicht(
            verkaeufe?.Bar ?? 0m,
            verkaeufe?.Karte ?? 0m,
            (verkaeufe?.Bar ?? 0m) + (verkaeufe?.Karte ?? 0m),
            verkaeufe?.Anzahl ?? 0,
            ausgaben.Sum(k => k.Betrag),
            ausgaben);
    }

    /// <summary>Die letzten Verkäufe eines Salons (für die Live-Ansicht des Besitzers).</summary>
    public async Task<List<VerkaufEintrag>> GetLetzteVerkaeufeAsync(Guid salonId, int anzahl = 25, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var verkaeufe = await db.Verkaeufe
            .AsNoTracking()
            .Include(v => v.Positionen)
            .Include(v => v.KassenSitzung).ThenInclude(k => k.Kassierer)
            .Where(v => v.SalonId == salonId)
            .OrderByDescending(v => v.ZeitUtc)
            .Take(anzahl)
            .ToListAsync(cancellationToken);

        return verkaeufe.Select(v => new VerkaufEintrag(
            v.Id,
            v.ZeitUtc,
            v.KassenSitzung.Kassierer.FullName,
            v.ZahlungsArt,
            v.GesamtSumme,
            string.Join(", ", v.Positionen.Select(p => $"{p.Menge}× {p.LeistungName}")))).ToList();
    }

    /// <summary>Die letzten Kassenabschlüsse eines Salons.</summary>
    public async Task<List<KassenSitzung>> GetAbschluesseAsync(Guid salonId, int anzahl = 20, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.KassenSitzungen
            .AsNoTracking()
            .Include(k => k.Kassierer)
            .Where(k => k.SalonId == salonId && k.Status == KassenSitzungStatus.Abgeschlossen)
            .OrderByDescending(k => k.AbgeschlossenUtc)
            .Take(anzahl)
            .ToListAsync(cancellationToken);
    }
}
