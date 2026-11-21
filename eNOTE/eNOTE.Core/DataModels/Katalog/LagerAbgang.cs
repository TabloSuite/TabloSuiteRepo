namespace ENote.Core.DataModels.Katalog;

public enum LagerAbgangArt
{
    /// <summary>Ware unbrauchbar (Bruch, Ablauf, Diebstahl …).</summary>
    Abschreibung = 0,

    /// <summary>Besitzer nimmt Ware für sich (kein Umsatz).</summary>
    Eigenentnahme = 1
}

public enum AbschreibungsGrund
{
    Casse = 0,
    Peremption = 1,
    Vol = 2,
    Autre = 3
}

/// <summary>
/// Bestand verlässt das Lager ohne Verkauf. Revisionssicher: Name und Preise werden als Kopie
/// gespeichert; der Wert zählt zum Einkaufspreis (nie als Umsatz).
/// </summary>
public class LagerAbgang
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public Guid LeistungId { get; set; }
    public Leistung Leistung { get; set; } = null!;
    public string LeistungName { get; set; } = string.Empty;
    public decimal Menge { get; set; }
    public LagerAbgangArt Art { get; set; }
    public AbschreibungsGrund? Grund { get; set; }
    public string? Notiz { get; set; }

    /// <summary>Einkaufspreis je Einheit zum Zeitpunkt des Abgangs (null = unbekannt).</summary>
    public decimal? EinkaufspreisSnapshot { get; set; }
    public decimal VerkaufspreisSnapshot { get; set; }

    /// <summary>Bei Eigenentnahme an der Kasse: die offene Sitzung.</summary>
    public Guid? KassenSitzungId { get; set; }
    public Guid ErfasstVonId { get; set; }
    public string ErfasstVonName { get; set; } = string.Empty;
    public DateTime ZeitUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;

    public decimal? Einkaufswert => EinkaufspreisSnapshot is decimal e ? Math.Round(e * Menge, 0) : null;
}
