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
        IReadOnlyList<KategorieAusgabe> MobileMoneyNachAnbieter,
        decimal Einlagen = 0,
        decimal EntnahmenBar = 0,
        decimal WarenEntnahmenWert = 0,
        decimal AbschreibungenWert = 0,
        decimal Erstattungen = 0)
    {
        /// <summary>Ventes + Apports/aides − Dépenses − Retraits d'espèces du propriétaire.</summary>
        public decimal Saldo => EinnahmenGesamt + Einlagen - AusgabenGesamt - EntnahmenBar;
    }

    public record KategorieAusgabe(string Name, decimal Betrag);

    public record VerkaufEintrag(
        Guid VerkaufId,
        DateTime ZeitUtc,
        string KassiererName,
        ZahlungsArt ZahlungsArt,
        decimal GesamtSumme,
        string PositionenText,
        IReadOnlyList<ZahlungsAnteil> Zahlungen,
        bool IstStorno = false,
        string? StornoGrund = null,
        decimal Erstattet = 0)
    {
        /// <summary>Verkauf, der bereits ganz erstattet wurde (Summe der Rückerstattungen = Bon).</summary>
        public bool VollErstattet => !IstStorno && Erstattet >= GesamtSumme && GesamtSumme > 0;

        /// <summary>Kurztext der Zahlung, z. B. „Espèces + Flooz“.</summary>
        public string ZahlungText => ZahlungsTexte.Kurz(Zahlungen);
    }

    /// <summary>Verkauf mit Zeit und Zahlungsanteilen (für Auswertungen).</summary>
    private sealed record VerkaufAnteile(DateTime ZeitUtc, IReadOnlyList<ZahlungsAnteil> Anteile, bool IstStorno = false)
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
                IstStorno = v.StornoVonVerkaufId != null,
                Zahlungen = v.Zahlungen.OrderBy(z => z.Reihenfolge)
                    .Select(z => new { z.Art, z.Betrag, z.AnbieterName }).ToList()
            })
            .ToListAsync(ct);

        return roh.Select(v => new VerkaufAnteile(v.ZeitUtc, KassenRechner.Anteile(v.ZahlungsArt, v.GesamtSumme,
            v.Zahlungen.Select(z => new ZahlungsAnteil(z.Art, z.Betrag, z.AnbieterName))), v.IstStorno)).ToList();
    }

    /// <summary>Tagesumsätze der letzten N Tage für einen Salon.</summary>
    /// <param name="versatz">Versatz der Ortszeit des Ladens zu UTC (Tage werden in Ortszeit gebildet).</param>
    public async Task<List<TagesUmsatz>> GetTagesUmsaetzeAsync(Guid salonId, int tage = 14, CancellationToken cancellationToken = default, TimeSpan versatz = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ab = DateTime.UtcNow.Date.AddDays(-tage);
        return GruppiereNachTag(await LadeAnteileAsync(db, salonId, ab, DateTime.MaxValue, cancellationToken), versatz);
    }

    /// <summary>Tagesumsätze in einem frei wählbaren Zeitraum (für Filter + PDF-Export).</summary>
    public async Task<List<TagesUmsatz>> GetTagesUmsaetzeImZeitraumAsync(
        Guid salonId, DateTime vonUtc, DateTime bisUtc, CancellationToken cancellationToken = default, TimeSpan versatz = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return GruppiereNachTag(await LadeAnteileAsync(db, salonId, vonUtc, bisUtc, cancellationToken), versatz);
    }

    private static List<TagesUmsatz> GruppiereNachTag(IEnumerable<VerkaufAnteile> verkaeufe, TimeSpan versatz)
        => verkaeufe
            .GroupBy(v => (v.ZeitUtc + versatz).Date)
            .Select(g => new TagesUmsatz(
                g.Key,
                g.Count(v => !v.IstStorno),
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

        var geld = await db.GeldBewegungen.AsNoTracking()
            .Where(g => g.SalonId == salonId && g.ZeitUtc >= vonUtc && g.ZeitUtc <= bisUtc)
            .Select(g => new { g.Art, g.Betrag }).ToListAsync(cancellationToken);
        var lager = await db.LagerAbgaenge.AsNoTracking()
            .Where(l => l.SalonId == salonId && l.ZeitUtc >= vonUtc && l.ZeitUtc <= bisUtc && l.EinkaufspreisSnapshot != null)
            .Select(l => new { l.Art, Wert = l.EinkaufspreisSnapshot!.Value * l.Menge }).ToListAsync(cancellationToken);

        return new FinanzUebersicht(
            Summe(ZahlungsArt.Bar),
            Summe(ZahlungsArt.Karte),
            Summe(ZahlungsArt.MobileMoney),
            anteile.Sum(a => a.Betrag),
            verkaeufe.Count(v => !v.IstStorno),
            ausgaben.Sum(k => k.Betrag),
            ausgaben,
            mobileNachAnbieter,
            geld.Where(g => g.Art == Core.DataModels.Finanzen.GeldBewegungArt.Einlage).Sum(g => g.Betrag),
            geld.Where(g => g.Art == Core.DataModels.Finanzen.GeldBewegungArt.EntnahmeBesitzer).Sum(g => g.Betrag),
            Math.Round(lager.Where(l => l.Art == Core.DataModels.Katalog.LagerAbgangArt.Eigenentnahme).Sum(l => l.Wert), 0),
            Math.Round(lager.Where(l => l.Art == Core.DataModels.Katalog.LagerAbgangArt.Abschreibung).Sum(l => l.Wert), 0),
            -verkaeufe.Where(v => v.IstStorno).Sum(v => v.Gesamt));
    }

    /// <summary>Die letzten Verkäufe eines Salons (für die Live-Ansicht des Besitzers).</summary>
    public async Task<List<VerkaufEintrag>> GetLetzteVerkaeufeAsync(Guid salonId, int anzahl = 25, CancellationToken cancellationToken = default)
        => (await SucheVerkaeufeAsync(salonId, null, null, null, null, 0, anzahl, cancellationToken)).Eintraege;

    /// <summary>
    /// Verkäufe seitenweise: optional im Zeitraum, mit Suche (Artikel oder Kassierer) und Zahlungsart-Filter –
    /// alles in der Datenbank, damit auch nach Jahren nur die angezeigte Seite geladen wird.
    /// </summary>
    public async Task<(List<VerkaufEintrag> Eintraege, bool Mehr)> SucheVerkaeufeAsync(
        Guid salonId, DateTime? vonUtc, DateTime? bisUtc, string? suche, ZahlungsArt? zahlungsArt,
        int ueberspringen, int anzahl = 50, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var abfrage = db.Verkaeufe.AsNoTracking().Where(v => v.SalonId == salonId);
        if (vonUtc is not null) abfrage = abfrage.Where(v => v.ZeitUtc >= vonUtc);
        if (bisUtc is not null) abfrage = abfrage.Where(v => v.ZeitUtc <= bisUtc);
        if (zahlungsArt is { } art) abfrage = abfrage.Where(v => v.Zahlungen.Any(z => z.Art == art));
        if (!string.IsNullOrWhiteSpace(suche))
        {
            var s = suche.Trim();
            abfrage = abfrage.Where(v => v.Positionen.Any(p => p.LeistungName.Contains(s))
                || (v.KassenSitzung.Kassierer.FullName != null && v.KassenSitzung.Kassierer.FullName.Contains(s)));
        }

        var verkaeufe = await abfrage
            .Include(v => v.Positionen)
            .Include(v => v.Zahlungen)
            .Include(v => v.KassenSitzung).ThenInclude(k => k.Kassierer)
            .OrderByDescending(v => v.ZeitUtc)
            .Skip(Math.Max(0, ueberspringen))
            .Take(anzahl + 1)
            .ToListAsync(cancellationToken);
        var mehr = verkaeufe.Count > anzahl;
        if (mehr) verkaeufe.RemoveAt(verkaeufe.Count - 1);

        var ids = verkaeufe.Select(v => v.Id).ToList();
        var erstattet = await db.Verkaeufe.AsNoTracking()
            .Where(v => v.StornoVonVerkaufId != null && ids.Contains(v.StornoVonVerkaufId.Value))
            .GroupBy(v => v.StornoVonVerkaufId!.Value)
            .Select(g => new { g.Key, Summe = g.Sum(v => v.GesamtSumme) })
            .ToDictionaryAsync(x => x.Key, x => -x.Summe, cancellationToken);

        return (verkaeufe.Select(v => new VerkaufEintrag(
            v.Id,
            v.ZeitUtc,
            v.KassenSitzung.Kassierer.FullName,
            v.ZahlungsArt,
            v.GesamtSumme,
            string.Join(", ", v.Positionen.Select(p => $"{Math.Abs(p.Menge)}× {p.LeistungName}")),
            KassenRechner.Anteile(v),
            v.IstStorno,
            v.StornoGrund,
            erstattet.GetValueOrDefault(v.Id))).ToList(), mehr);
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
