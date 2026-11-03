namespace ENote.Core.DataModels.Katalog;

/// <summary>
/// Eine kassierbare Leistung (Dienstleistung wie „Coupe“) oder ein Verkaufsartikel
/// (z. B. Erfrischungsgetränk) mit optionalem Lagerbestand.
/// </summary>
public class Leistung
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public Salons.Salon Salon { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string? Beschreibung { get; set; }
    public decimal Preis { get; set; }

    /// <summary>Optionaler Strichcode (EAN/Code), den der Kassierer einscannen kann.</summary>
    public string? StrichCode { get; set; }

    /// <summary>true = Artikel mit Stückzahl/Bestand; false = reine Dienstleistung.</summary>
    public bool HatStueckzahl { get; set; }

    /// <summary>Aktueller Lagerbestand (nur bei <see cref="HatStueckzahl"/> relevant).</summary>
    public int Stueckzahl { get; set; }

    /// <summary>Ab diesem Bestand (oder darunter) wird ein „bald leer“-Hinweis gezeigt.</summary>
    public int? MindestbestandWarnung { get; set; }

    public bool IstAktiv { get; set; } = true;

    /// <summary>Optionales Bild aus dem Blob-Speicher.</summary>
    public Guid? BildAssetId { get; set; }
    public Infrastruktur.BlobMediaAsset? BildAsset { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
