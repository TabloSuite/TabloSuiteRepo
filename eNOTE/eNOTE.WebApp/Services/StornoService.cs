using ENote.Core.DataModels.Kasse;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Rückerstattung (Storno) als Gegenbuchung: Ein Verkauf wird nie gelöscht oder geändert. Stattdessen
/// entsteht in der aktuell offenen Kasse ein eigener Bon mit negativen Mengen und Beträgen, der auf den
/// Originalbon verweist. Umsätze, Bargeld-Soll und Bestseller heben sich dadurch automatisch auf.
/// Teil-Erstattungen (einzelne Zeilen oder Mengen) sind möglich, bis alles erstattet ist.
/// </summary>
public class StornoService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    KassenService kassenService,
    IUmsatzBenachrichtigungService umsatzBenachrichtigung)
{
    public static readonly string[] GrundVorschlaege = ["Erreur de caisse", "Article défectueux", "Client mécontent", "Prestation non réalisée"];

    public record StornoPosition(Guid PositionId, string Name, decimal EinzelPreis, int Menge, int ErstattetMenge,
        decimal ZeilenSumme, decimal ErstattetBetrag, bool HatLager)
    {
        public int Offen => Menge - ErstattetMenge;
    }

    /// <summary>Ein Weg, auf dem erstattet werden kann (= ein Zahlungsweg des Originals).</summary>
    public record StornoWeg(ZahlungsArt Art, Guid? AnbieterId, string Name, decimal Bezahlt, decimal Erstattet)
    {
        public decimal Offen => StornoRechner.Rest(Bezahlt, Erstattet);
        public string Schluessel => $"{(int)Art}:{AnbieterId}";
    }

    public record StornoVorlage(Guid VerkaufId, DateTime ZeitUtc, string KassiererName, decimal GesamtSumme,
        IReadOnlyList<StornoPosition> Positionen, IReadOnlyList<StornoWeg> Wege, decimal BereitsErstattet, bool BrauchtPin)
    {
        public bool VollstaendigErstattet => Positionen.All(p => p.Offen <= 0);
    }

    public record StornoPositionInput(Guid PositionId, int Menge);

    public record StornoErgebnis(bool Erfolg, string? Fehler, Verkauf? Storno = null, decimal Betrag = 0m);

    /// <summary>Lädt einen Verkauf mit allem, was der Erstattungsdialog braucht (offene Mengen, Zahlwege).</summary>
    public async Task<StornoVorlage?> GetVorlageAsync(Guid verkaufId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var verkauf = await LadeOriginalAsync(db, verkaufId, ct);
        if (verkauf is null || verkauf.IstStorno) return null;

        var storni = await LadeStorniAsync(db, verkaufId, ct);
        var kassierer = await db.KassenSitzungen.Where(k => k.Id == verkauf.KassenSitzungId)
            .Select(k => k.Kassierer.FullName).FirstOrDefaultAsync(ct) ?? "—";
        return BaueVorlage(verkauf, storni, kassierer, await kassenService.HatBesitzerPinAsync(verkauf.SalonId, ct));
    }

    /// <summary>
    /// Erstattet die gewählten Mengen über einen Zahlweg des Originals. Freigabe durch den Besitzer
    /// (PIN oder Besitzer selbst). Bar muss in der offenen Kasse vorhanden sein.
    /// </summary>
    public async Task<StornoErgebnis> ErstatteAsync(
        Guid userId,
        Guid sitzungId,
        Guid verkaufId,
        IReadOnlyList<StornoPositionInput> positionen,
        string wegSchluessel,
        bool zurueckInsLager,
        string? grund,
        string? besitzerPin,
        CancellationToken ct = default)
    {
        grund = grund?.Trim();
        if (string.IsNullOrWhiteSpace(grund)) return new(false, "Indiquez le motif du remboursement.");
        var gewaehlt = positionen.Where(p => p.Menge > 0).ToList();
        if (gewaehlt.Count == 0) return new(false, "Choisissez au moins un article à rembourser.");

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var sitzung = await db.KassenSitzungen.AsNoTracking()
            .FirstOrDefaultAsync(k => k.Id == sitzungId && k.Status == KassenSitzungStatus.Offen, ct);
        if (sitzung is null) return new(false, "Ouvrez la caisse pour rembourser.");

        var original = await LadeOriginalAsync(db, verkaufId, ct);
        if (original is null || original.SalonId != sitzung.SalonId) return new(false, "Vente introuvable.");
        if (original.IstStorno) return new(false, "Un remboursement ne peut pas être remboursé.");

        if (await kassenService.PruefeBesitzerFreigabeAsync(sitzung.SalonId, userId, besitzerPin, ct) is string freigabeFehler)
            return new(false, freigabeFehler);

        var storni = await LadeStorniAsync(db, verkaufId, ct);
        var vorlage = BaueVorlage(original, storni, string.Empty, false);

        var weg = vorlage.Wege.FirstOrDefault(w => w.Schluessel == wegSchluessel);
        if (weg is null) return new(false, "Choisissez comment rembourser le client.");

        var storno = new Verkauf
        {
            KassenSitzungId = sitzung.Id,
            SalonId = sitzung.SalonId,
            ZeitUtc = DateTime.UtcNow,
            ZahlungsArt = weg.Art,
            StornoVonVerkaufId = original.Id,
            KundeId = original.KundeId,
            StornoGrund = grund.Length > 300 ? grund[..300] : grund,
            StornoErfasstVonName = await db.Users.Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "—",
            KarteZahlungBestaetigt = weg.Art == ZahlungsArt.Karte
        };

        decimal brutto = 0m, rabatt = 0m, betrag = 0m;
        foreach (var eingabe in gewaehlt)
        {
            var pos = original.Positionen.FirstOrDefault(p => p.Id == eingabe.PositionId);
            var info = vorlage.Positionen.FirstOrDefault(p => p.PositionId == eingabe.PositionId);
            if (pos is null || info is null) return new(false, "Article introuvable sur le reçu.");
            if (eingabe.Menge > info.Offen)
                return new(false, $"« {pos.LeistungName} » : {info.Offen} au maximum à rembourser.");

            var zeile = StornoRechner.Betrag(pos.ZeilenSumme, pos.Menge, info.ErstattetMenge, info.ErstattetBetrag, eingabe.Menge);
            var zeilenBrutto = pos.EinzelPreis * eingabe.Menge;
            brutto += zeilenBrutto;
            rabatt += zeilenBrutto - zeile;
            betrag += zeile;

            storno.Positionen.Add(new VerkaufsPosition
            {
                LeistungId = pos.LeistungId,
                LeistungName = pos.LeistungName,
                EinzelPreis = pos.EinzelPreis,
                Menge = -eingabe.Menge,
                RabattId = pos.RabattId,
                RabattBetrag = -(zeilenBrutto - zeile),
                ZeilenSumme = -zeile,
                StornoVonPositionId = pos.Id
            });

            if (zurueckInsLager)
            {
                var leistung = pos.Leistung;
                var ziel = leistung.BasisLeistung ?? leistung;
                if (ziel.HatStueckzahl)
                {
                    ziel.Stueckzahl += leistung.BasisLeistungId is not null && leistung.BasisAbzugMenge is decimal je
                        ? je * eingabe.Menge
                        : eingabe.Menge;
                }
            }
        }

        betrag = Math.Round(betrag, 2);
        if (betrag <= 0) return new(false, "Rien à rembourser.");
        if (betrag > weg.Offen)
            return new(false, $"Au maximum {weg.Offen:N0} F peuvent être remboursés en {weg.Name}. Faites un 2e remboursement pour le reste.");

        if (weg.Art == ZahlungsArt.Bar)
        {
            var barSoll = await kassenService.GetBarSollAsync(sitzung.Id, ct) ?? 0m;
            if (betrag > barSoll)
                return new(false, $"Pas assez d'espèces en caisse ({barSoll:N0} F attendus) pour rembourser {betrag:N0} F.");
        }

        storno.Zwischensumme = -Math.Round(brutto, 2);
        storno.RabattSumme = -Math.Round(rabatt, 2);
        storno.GesamtSumme = -betrag;
        storno.Zahlungen.Add(new VerkaufZahlung
        {
            Art = weg.Art,
            Betrag = -betrag,
            MobileMoneyAnbieterId = weg.AnbieterId,
            AnbieterName = weg.Art == ZahlungsArt.MobileMoney ? weg.Name : null,
            Reihenfolge = 0
        });

        db.Verkaeufe.Add(storno);
        await db.SaveChangesAsync(ct);

        try
        {
            var heute = DateTime.UtcNow.Date;
            var tagesUmsatz = await db.Verkaeufe.Where(v => v.SalonId == sitzung.SalonId && v.ZeitUtc >= heute).SumAsync(v => v.GesamtSumme, ct);
            var anzahl = await db.Verkaeufe.CountAsync(v => v.SalonId == sitzung.SalonId && v.ZeitUtc >= heute && v.StornoVonVerkaufId == null, ct);
            await umsatzBenachrichtigung.VerkaufGemeldetAsync(sitzung.SalonId, tagesUmsatz, anzahl, ct);
        }
        catch
        {
            // Live-Benachrichtigung darf die Erstattung nie blockieren.
        }

        return new(true, null, storno, betrag);
    }

    private static Task<Verkauf?> LadeOriginalAsync(ApplicationDbContext db, Guid verkaufId, CancellationToken ct)
        => db.Verkaeufe
            .Include(v => v.Positionen).ThenInclude(p => p.Leistung).ThenInclude(l => l.BasisLeistung)
            .Include(v => v.Zahlungen)
            .FirstOrDefaultAsync(v => v.Id == verkaufId, ct);

    private static Task<List<Verkauf>> LadeStorniAsync(ApplicationDbContext db, Guid verkaufId, CancellationToken ct)
        => db.Verkaeufe.AsNoTracking()
            .Include(v => v.Positionen)
            .Include(v => v.Zahlungen)
            .Where(v => v.StornoVonVerkaufId == verkaufId)
            .ToListAsync(ct);

    private static StornoVorlage BaueVorlage(Verkauf original, IReadOnlyList<Verkauf> storni, string kassierer, bool brauchtPin)
    {
        var stornoPositionen = storni.SelectMany(s => s.Positionen).ToList();
        var positionen = original.Positionen
            .Select(p =>
            {
                var erstattet = stornoPositionen.Where(s => s.StornoVonPositionId == p.Id).ToList();
                var ziel = p.Leistung?.BasisLeistung ?? p.Leistung;
                return new StornoPosition(p.Id, p.LeistungName, p.EinzelPreis, p.Menge,
                    -erstattet.Sum(s => s.Menge), p.ZeilenSumme, -erstattet.Sum(s => s.ZeilenSumme), ziel?.HatStueckzahl == true);
            })
            .ToList();

        var stornoZahlungen = storni.SelectMany(s => s.Zahlungen).ToList();
        var wege = KassenRechner.Anteile(original)
            .Select((a, i) => (Anteil: a, Zahlung: original.Zahlungen.OrderBy(z => z.Reihenfolge).ElementAtOrDefault(i)))
            .GroupBy(x => (x.Anteil.Art, AnbieterId: x.Zahlung?.MobileMoneyAnbieterId))
            .Select(g => new StornoWeg(g.Key.Art, g.Key.AnbieterId, ZahlungsTexte.Name(g.First().Anteil),
                g.Sum(x => x.Anteil.Betrag),
                -stornoZahlungen.Where(z => z.Art == g.Key.Art && z.MobileMoneyAnbieterId == g.Key.AnbieterId).Sum(z => z.Betrag)))
            .ToList();

        return new StornoVorlage(original.Id, original.ZeitUtc, kassierer, original.GesamtSumme, positionen, wege,
            -storni.Sum(s => s.GesamtSumme), brauchtPin);
    }
}
