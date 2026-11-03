namespace ENote.Core.DataModels.Salons;

/// <summary>
/// Ein Mandant der Plattform: ein Friseursalon (oder vergleichbares Geschäft).
/// </summary>
public class Salon
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Standort { get; set; }
    public string? Beschreibung { get; set; }
    public bool IstAktiv { get; set; } = true;

    /// <summary>Telefonnummer des Geschäfts (erscheint auf dem Bon).</summary>
    public string? TelefonNummer { get; set; }

    /// <summary>Adresse des Geschäfts (erscheint auf dem Bon).</summary>
    public string? Adresse { get; set; }

    /// <summary>
    /// Optionales Link-Ziel für den QR-Code auf dem Bon (z. B. WhatsApp, Bewertung).
    /// Leer = QR verweist auf die digitale Bon-Kopie.
    /// </summary>
    public string? BonQrLink { get; set; }

    /// <summary>Registrierung wurde vom SuperAdmin noch nicht angenommen.</summary>
    public bool IstFreigegeben { get; set; }

    /// <summary>Individueller monatlicher Nutzungspreis; null = globaler Standard gilt.</summary>
    public decimal? IndividuellerMonatsPreis { get; set; }

    /// <summary>Freier Slogan des Salons (z. B. „In God we trust“), erscheint auf dem Bon.</summary>
    public string? Slogan { get; set; }

    /// <summary>Optionales Logo aus dem Blob-Speicher, erscheint auf dem Bon.</summary>
    public Guid? LogoAssetId { get; set; }
    public Infrastruktur.BlobMediaAsset? LogoAsset { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<UserSalon> UserSalons { get; set; } = new List<UserSalon>();
}
