namespace ENote.Core.DataModels.Finanzen;

/// <summary>
/// Eine Ausgabe des Ladens. Beim Erfassen nur Betrag, Datum, Kategorie und
/// optional ein Beleg (PDF/Bild aus dem Blob-Speicher).
/// </summary>
public class Ausgabe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public Salons.Salon Salon { get; set; } = null!;

    public decimal Betrag { get; set; }
    public DateTime DatumUtc { get; set; }

    public Guid KategorieId { get; set; }
    public AusgabenKategorie Kategorie { get; set; } = null!;

    /// <summary>Optionale Notiz.</summary>
    public string? Notiz { get; set; }

    /// <summary>Optionaler Beleg (PDF/Bild) aus dem Blob-Speicher.</summary>
    public Guid? BelegAssetId { get; set; }
    public Infrastruktur.BlobMediaAsset? BelegAsset { get; set; }

    public Guid ErfasstVonId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;
}
