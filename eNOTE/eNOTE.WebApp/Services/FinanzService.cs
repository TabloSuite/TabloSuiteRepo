using ENote.Core.DataModels.Kasse;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Besitzer-Finanzansicht: Umsatzübersicht, Verkaufsliste und Kassenabschlüsse
/// eines Salons. Optional für delegierte Kassierer mit Finanzrecht.
/// </summary>
public class FinanzService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public record TagesUmsatz(DateTime Datum, int AnzahlVerkaeufe, decimal Bar, decimal Karte, decimal MobileMoney, decimal Gesamt);

    /// <summary>Einnahmen, Ausgaben und Saldo (Solde de caisse) eines Zeitraums.</summary>
    public record FinanzUebersicht(
        decimal EinnahmenBar,
        decimal EinnahmenKarte,
        decimal EinnahmenMobileMoney,
        decimal EinnahmenGesamt,
        int AnzahlVerkaeufe,
        decimal AusgabenGesamt,
        IReadOnlyList<KategorieAusgabe> AusgabenNachKategorie,
        IReadOnlyList<KategorieAusgabe> MobileMoneyNachAnbieter)
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
        string PositionenText,
        IReadOnlyList<ZahlungsAnteil> Zahlungen)
    {
        /// <summary>Kurztext der Zahlung, z. B. „Espèces + Flooz“.</summary>
        public string ZahlungText => ZahlungsTexte.Kurz(Zahlungen);
    }

    /// <summary>Verkauf mit Zeit und Zahlungsanteilen (für Auswertungen).</summary>
    private sealed record VerkaufAnteile(DateTime ZeitUtc, IReadOnlyList<ZahlungsAnteil> Anteile)
    {
        public decimal Summe(ZahlungsArt art) => Anteile.Where(a => a.Art == art).Sum(a => a.Betrag);
        public decimal Gesamt => Anteile.Sum(a => a.Betrag);
    }

    /// <summary>Lädt Verkäufe eines Zeitraums mit ihren Zahlungsanteilen (Altbestand ohne Anteile = Gesamtsumme).</summary>
    private static async Task<List<VerkaufAnteile>> LadeAnteileAsync(
        ApplicationDbContext db, Guid salonId, DateTime vonUtc, DateTime bisUtc, CancellationToken ct)
    {
        var roh = await db.Verkaeufe
            .AsNoTracking()
            .Where(v => v.SalonId == salonId && v.ZeitUtc >= vonUtc && v.ZeitUtc <= bisUtc)
            .Select(v => new
            {
                v.ZeitUtc,
                v.ZahlungsArt,
                v.GesamtSumme,
                Zahlungen = v.Zahlungen.OrderBy(z => z.Reihenfolge)
                    .Select(z => new { z.Art, z.Betrag, z.AnbieterName }).ToList()
            })
            .ToListAsync(ct);

        return roh.Select(v => new VerkaufAnteile(v.ZeitUtc, KassenRechner.Anteile(v.ZahlungsArt, v.GesamtSumme,
            v.Zahlungen.Select(z => new ZahlungsAnteil(z.Art, z.Betrag, z.AnbieterName))))).ToList();
    }

    /// <summary>Tagesumsätze der letzten N Tage für einen Salon.</summary>
    public async Task<List<TagesUmsatz>> GetTagesUmsaetzeAsync(Guid salonId, int tage = 14, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ab = DateTime.UtcNow.Date.AddDays(-tage);
        return GruppiereNachTag(await LadeAnteileAsync(db, salonId, ab, DateTime.MaxValue, cancellationToken));
    }

    /// <summary>Tagesumsätze in einem frei wählbaren Zeitraum (für Filter + PDF-Export).</summary>
    public async Task<List<TagesUmsatz>> GetTagesUmsaetzeImZeitraumAsync(
        Guid salonId, DateTime vonUtc, DateTime bisUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return GruppiereNachTag(await LadeAnteileAsync(db, salonId, vonUtc, bisUtc, cancellationToken));
    }

    private static List<TagesUmsatz> GruppiereNachTag(IEnumerable<VerkaufAnteile> verkaeufe)
        => verkaeufe
            .GroupBy(v => v.ZeitUtc.Date)
            .Select(g => new TagesUmsatz(
                g.Key,
                g.Count(),
                g.Sum(v => v.Summe(ZahlungsArt.Bar)),
                g.Sum(v => v.Summe(ZahlungsArt.Karte)),
                g.Sum(v => v.Summe(ZahlungsArt.MobileMoney)),
                g.Sum(v => v.Gesamt)))
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

        var verkaeufe = await LadeAnteileAsync(db, salonId, vonUtc, bisUtc, cancellationToken);
        var anteile = verkaeufe.SelectMany(v => v.Anteile).ToList();
        decimal Summe(ZahlungsArt art) => anteile.Where(a => a.Art == art).Sum(a => a.Betrag);
        var mobileNachAnbieter = anteile
            .Where(a => a.Art == ZahlungsArt.MobileMoney)
            .GroupBy(a => a.AnbieterName ?? "Mobile Money")
            .Select(g => new KategorieAusgabe(g.Key, g.Sum(a => a.Betrag)))
            .OrderByDescending(x => x.Betrag)
            .ToList();

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
            Summe(ZahlungsArt.Bar),
            Summe(ZahlungsArt.Karte),
            Summe(ZahlungsArt.MobileMoney),
            anteile.Sum(a => a.Betrag),
            verkaeufe.Count,
            ausgaben.Sum(k => k.Betrag),
            ausgaben,
            mobileNachAnbieter);
    }

    /// <summary>Die letzten Verkäufe eines Salons (für die Live-Ansicht des Besitzers).</summary>
    public async Task<List<VerkaufEintrag>> GetLetzteVerkaeufeAsync(Guid salonId, int anzahl = 25, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var verkaeufe = await db.Verkaeufe
            .AsNoTracking()
            .Include(v => v.Positionen)
            .Include(v => v.Zahlungen)
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
            string.Join(", ", v.Positionen.Select(p => $"{p.Menge}× {p.LeistungName}")),
            KassenRechner.Anteile(v))).ToList();
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
