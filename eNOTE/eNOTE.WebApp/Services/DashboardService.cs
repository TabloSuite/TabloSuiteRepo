using ENote.Core.DataModels.Kasse;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Kennzahlen für die Startseite eines Ladens: Tagesumsatz im Vergleich, Verlauf,
/// Zahlungsmix, Bestseller, Lagerwarnungen und Kassenstatus. Nur für Besitzer oder
/// Mitglieder mit Finanzrecht (serverseitig geprüft).
/// </summary>
public class DashboardService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public const int VerlaufTage = 14;
    public const int AuswertungTage = 30;

    public record TagesWert(DateTime Datum, decimal Umsatz, int Verkaeufe);
    public record Bestseller(string Name, int Menge, decimal Umsatz);
    public record LagerWarnung(Guid LeistungId, string Name, decimal Bestand, decimal Mindestbestand);
    public record OffeneKasse(string Kassierer, DateTime GestartetUtc, decimal BarSoll);

    public record Daten(
        decimal UmsatzHeute,
        int VerkaeufeHeute,
        decimal DurchschnittTag,
        IReadOnlyList<TagesWert> Verlauf,
        decimal Bar, decimal Karte, decimal MobileMoney,
        IReadOnlyList<Bestseller> Bestseller,
        IReadOnlyList<LagerWarnung> Lager,
        OffeneKasse? Kasse)
    {
        public decimal PanierMoyen => VerkaeufeHeute == 0 ? 0 : Math.Round(UmsatzHeute / VerkaeufeHeute, 0);

        /// <summary>Veränderung heute gegenüber dem Tagesdurchschnitt (null ohne Vergleichsbasis).</summary>
        public decimal? VeraenderungProzent => DurchschnittTag <= 0 ? null : Math.Round((UmsatzHeute - DurchschnittTag) / DurchschnittTag * 100, 0);
    }

    /// <summary>Darf der Benutzer die Kennzahlen des Ladens sehen (Besitzer oder Finanzrecht)?</summary>
    public async Task<bool> DarfSehenAsync(Guid benutzerId, Guid salonId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        return await LadenZugriff.IstBesitzerAsync(db, benutzerId, salonId, ct)
            || await db.UserSalons.AnyAsync(us => us.AppUserId == benutzerId && us.SalonId == salonId
                && us.KannFinanzenSehen && !us.AppUser.IsBlocked && us.Salon.IstAktiv, ct);
    }

    /// <param name="heuteLokal">Lokales Datum „heute“ (Tagesgrenzen der Anzeige).</param>
    /// <param name="utcOffset">Versatz Lokalzeit − UTC, um Tagesgrenzen korrekt zu bilden.</param>
    public async Task<Daten?> LadeAsync(Guid benutzerId, Guid salonId, DateTime heuteLokal, TimeSpan utcOffset, CancellationToken ct = default)
    {
        if (!await DarfSehenAsync(benutzerId, salonId, ct))
        {
            return null;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var heute = heuteLokal.Date;
        var abLokal = heute.AddDays(-(AuswertungTage - 1));
        var abUtc = DateTime.SpecifyKind(abLokal - utcOffset, DateTimeKind.Utc);

        var verkaeufe = await db.Verkaeufe.AsNoTracking()
            .Where(v => v.SalonId == salonId && v.ZeitUtc >= abUtc)
            .Select(v => new
            {
                v.ZeitUtc, v.ZahlungsArt, v.GesamtSumme,
                IstStorno = v.StornoVonVerkaufId != null,
                Zahlungen = v.Zahlungen.Select(z => new { z.Art, z.Betrag }).ToList()
            })
            .ToListAsync(ct);

        var proTag = verkaeufe
            .GroupBy(v => (v.ZeitUtc + utcOffset).Date)
            .ToDictionary(g => g.Key, g => (Umsatz: g.Sum(v => v.GesamtSumme), Anzahl: g.Count(v => !v.IstStorno)));

        var verlauf = Enumerable.Range(0, VerlaufTage)
            .Select(i => heute.AddDays(-(VerlaufTage - 1) + i))
            .Select(d => new TagesWert(d, proTag.GetValueOrDefault(d).Umsatz, proTag.GetValueOrDefault(d).Anzahl))
            .ToList();

        // Vergleichsbasis: Durchschnitt der Tage mit Verkäufen in den letzten 4 Wochen (ohne heute).
        var vergleich = proTag.Where(kv => kv.Key < heute && kv.Key >= heute.AddDays(-28)).Select(kv => kv.Value.Umsatz).ToList();
        var durchschnitt = vergleich.Count == 0 ? 0m : Math.Round(vergleich.Average(), 0);

        var anteile = verkaeufe.SelectMany(v => KassenRechner.Anteile(v.ZahlungsArt, v.GesamtSumme,
            v.Zahlungen.Select(z => new ZahlungsAnteil(z.Art, z.Betrag)))).ToList();
        decimal Summe(ZahlungsArt art) => anteile.Where(a => a.Art == art).Sum(a => a.Betrag);

        var bestseller = (await db.VerkaufsPositionen.AsNoTracking()
                .Where(p => p.Verkauf.SalonId == salonId && p.Verkauf.ZeitUtc >= abUtc)
                .GroupBy(p => p.LeistungName)
                .Select(g => new { Name = g.Key, Menge = g.Sum(p => p.Menge), Umsatz = g.Sum(p => p.ZeilenSumme) })
                .OrderByDescending(b => b.Umsatz)
                .Take(5)
                .ToListAsync(ct))
            .Select(b => new Bestseller(b.Name, b.Menge, b.Umsatz))
            .ToList();

        var lager = await db.Leistungen.AsNoTracking()
            .Where(l => l.SalonId == salonId && l.IstAktiv && l.HatStueckzahl
                && l.MindestbestandWarnung != null && l.Stueckzahl <= l.MindestbestandWarnung)
            .OrderBy(l => l.Stueckzahl)
            .Select(l => new LagerWarnung(l.Id, l.Name, l.Stueckzahl, l.MindestbestandWarnung!.Value))
            .Take(6)
            .ToListAsync(ct);

        OffeneKasse? kasse = null;
        var sitzung = await db.KassenSitzungen.AsNoTracking()
            .Include(k => k.Kassierer)
            .Include(k => k.Verkaeufe).ThenInclude(v => v.Zahlungen)
            .FirstOrDefaultAsync(k => k.SalonId == salonId && k.Status == KassenSitzungStatus.Offen, ct);
        if (sitzung is not null)
        {
            kasse = new OffeneKasse(sitzung.Kassierer.FullName, sitzung.GestartetUtc,
                KassenRechner.BerechneBarSoll(sitzung.Startbestand, sitzung.Verkaeufe,
                    await db.GeldBewegungen.AsNoTracking().Where(g => g.KassenSitzungId == sitzung.Id).ToListAsync(ct)));
        }

        var heuteWert = proTag.GetValueOrDefault(heute);
        return new Daten(heuteWert.Umsatz, heuteWert.Anzahl, durchschnitt, verlauf,
            Summe(ZahlungsArt.Bar), Summe(ZahlungsArt.Karte), Summe(ZahlungsArt.MobileMoney),
            bestseller, lager, kasse);
    }
}
