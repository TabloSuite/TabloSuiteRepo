namespace ENote.Core.DataModels.Kasse;

public enum ZahlungsArt
{
    Bar = 0,
    Karte = 1
}

/// <summary>
/// Ein abgeschlossener Bezahlvorgang (Bon). Revisionssicher: wird nach dem
/// Buchen nicht mehr verändert.
/// </summary>
public class Verkauf
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid KassenSitzungId { get; set; }
    public KassenSitzung KassenSitzung { get; set; } = null!;
    public Guid SalonId { get; set; }

    public DateTime ZeitUtc { get; set; } = DateTime.UtcNow;
    public ZahlungsArt ZahlungsArt { get; set; }

    public decimal Zwischensumme { get; set; }
    public decimal RabattSumme { get; set; }
    public decimal GesamtSumme { get; set; }

    /// <summary>Nur bei Barzahlung: vom Kunden gegebener Betrag.</summary>
    public decimal? Gegeben { get; set; }

    /// <summary>Nur bei Barzahlung: berechnetes Rückgeld.</summary>
    public decimal? Rueckgeld { get; set; }

    /// <summary>Bei Kartenzahlung: Kassierer hat den externen Kartenvorgang bestätigt.</summary>
    public bool KarteZahlungBestaetigt { get; set; }

    public ICollection<VerkaufsPosition> Positionen { get; set; } = new List<VerkaufsPosition>();
}
