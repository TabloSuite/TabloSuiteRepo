using System.Text.Json;
using ENote.Core.DataModels.Katalog;
using ENote.Core.Services.Kasse;

namespace ENote.WebApp.Services;

/// <summary>
/// F5 in der Kasse (21.11.2026): Das angefangene Ticket steht im Browser DIESES Geräts (<c>localStorage</c>, ein Eintrag je
/// Laden, gebunden an die Kassensitzung) und wird nach dem Neuladen wiederhergestellt – kein Server-Speicher. Gemerkt werden
/// nur neue Zeilen (Leistung, Menge, freier Betrag), Kunde und Remise; Preise kommen beim Wiederherstellen aus dem Katalog
/// (wie beim Fortsetzen eines Tickets « en attente »), inaktive/gelöschte Leistungen fallen weg, die Remise wird neu geprüft.
/// Andere Kassensitzung (Abschluss, neue Kasse) → nichts anbieten. Die Kasse löscht den Eintrag nach Verkauf, Abbruch,
/// Parken, Abschluss und wenn ein anderes Gerät die Kasse übernimmt.
/// </summary>
public static class TicketMerker
{
    public const int Version = 1;

    /// <summary>Schlüssel im localStorage des Geräts (einer je Laden).</summary>
    public static string Schluessel(Guid salonId) => $"enote_ticket_{salonId:N}";

    public sealed record Zeile(Guid L, int M, decimal? B, decimal P);

    /// <summary>Gemerktes Ticket (kurze Namen: steht so im Browser). <c>P</c> = Preis beim Merken, nur zum Erkennen von Änderungen.</summary>
    public sealed record Gemerkt(int V, Guid S, Guid? K, Guid? R, bool Rb, IReadOnlyList<Zeile> Z);

    public sealed record WiederZeile(Leistung Leistung, int Menge, decimal? FreierBetrag);

    /// <summary>Ergebnis: Zeilen mit Leistungen des aktuellen Katalogs; <c>Ausgelassen</c> = nicht mehr verkäufliche Zeilen.</summary>
    public sealed record Wiederherstellung(IReadOnlyList<WiederZeile> Zeilen, int Ausgelassen, bool PreiseGeaendert,
        Guid? KundeId, Guid? RabattId, bool RabattBedingungBestaetigt);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = null };

    public static string Serialisiere(Gemerkt ticket) => JsonSerializer.Serialize(ticket, Json);

    /// <summary>Liest den Eintrag; Unlesbares (alte Version, manipuliert) → null.</summary>
    public static Gemerkt? Lese(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 100_000) return null;
        try
        {
            var t = JsonSerializer.Deserialize<Gemerkt>(json, Json);
            return t is { V: Version, Z: not null } ? t : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Prüft das gemerkte Ticket gegen die laufende Kassensitzung und den aktuellen Katalog (nur aktive Leistungen/Remises).
    /// null = nichts anzubieten (anderes/kein Ticket, andere Kassensitzung, keine verkäufliche Zeile mehr).
    /// </summary>
    public static Wiederherstellung? Pruefe(Gemerkt? gemerkt, Guid sitzungId, IEnumerable<Leistung> katalog, IEnumerable<Rabatt> rabatte)
    {
        if (gemerkt is null || gemerkt.S != sitzungId || gemerkt.Z.Count == 0) return null;
        var leistungen = katalog.Where(l => l.IstAktiv).GroupBy(l => l.Id).ToDictionary(g => g.Key, g => g.First());
        var zeilen = new List<WiederZeile>();
        var ausgelassen = 0;
        var preiseGeaendert = false;
        foreach (var z in gemerkt.Z.Take(TicketParkRegeln.PositionenMax))
        {
            if (z.M < 1 || !leistungen.TryGetValue(z.L, out var l)
                || (z.B is { } b && (b <= 0 || !l.IstStandardArtikel)))
            {
                ausgelassen++;
                continue;
            }
            if (z.B is null && l.Preis != z.P) preiseGeaendert = true;
            zeilen.Add(new WiederZeile(l, Math.Min(z.M, TicketParkRegeln.MengeMax), z.B));
        }
        ausgelassen += Math.Max(0, gemerkt.Z.Count - TicketParkRegeln.PositionenMax);
        if (zeilen.Count == 0) return null;

        // Remise neu prüfen: noch aktiv, und eine leistungsbezogene nur, wenn ihre Leistung noch im Ticket ist.
        var rabatt = gemerkt.R is Guid rid ? rabatte.FirstOrDefault(r => r.Id == rid && r.IstAktiv) : null;
        if (rabatt?.LeistungId is Guid nurFuer && zeilen.All(z => z.Leistung.Id != nurFuer)) rabatt = null;

        return new Wiederherstellung(zeilen, ausgelassen, preiseGeaendert, gemerkt.K, rabatt?.Id, rabatt is not null && gemerkt.Rb);
    }
}
