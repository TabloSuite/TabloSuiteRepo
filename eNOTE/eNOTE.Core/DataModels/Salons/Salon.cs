namespace ENote.Core.DataModels.Salons;

/// <summary>
/// Ein Mandant der Plattform: ein Friseursalon (oder vergleichbares Geschäft).
/// </summary>
public class Salon
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Standort { get; set; }

    /// <summary>Land des Geschäfts (z. B. Togo, Frankreich), für Filter und regionale Auswertung.</summary>
    public string? Land { get; set; }

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

    /// <summary>
    /// Ende der kostenlosen Probezeit (3 Kalendermonate, das Startmonat zählt mit).
    /// Null = keine Probezeit / bereits zahlend.
    /// </summary>
    public DateTime? ProbeBisUtc { get; set; }

    /// <summary>Hinweis « Fin de l'essai gratuit » (7 Tage vorher) versendet.</summary>
    public DateTime? ProbeEndeMailUtc { get; set; }

    /// <summary>Individueller monatlicher Nutzungspreis; null = globaler Standard gilt.</summary>
    public decimal? IndividuellerMonatsPreis { get; set; }

    public Abo.AboIntervall VertragsIntervall { get; set; } = Abo.AboIntervall.Monatlich;
    public Abo.AboIntervall? NaechstesVertragsIntervall { get; set; }
    public DateTime? VertragsBeginnUtc { get; set; }
    public DateTime? VertragsWechselAbUtc { get; set; }

    /// <summary>Freier Slogan des Salons (z. B. „In God we trust“), erscheint auf dem Bon.</summary>
    public string? Slogan { get; set; }

    /// <summary>
    /// true = der Besitzer hat die akzeptierten Mobile-Money-Anbieter selbst gewählt
    /// (<see cref="SalonMobileMoneyAnbieter"/>, auch leer = keine). false = alle aktiven
    /// Anbieter des Landes.
    /// </summary>
    public bool MobileMoneyIndividuell { get; set; }

    /// <summary>Optionales Logo aus dem Blob-Speicher, erscheint auf dem Bon.</summary>
    public Guid? LogoAssetId { get; set; }
    public Infrastruktur.BlobMediaAsset? LogoAsset { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<UserSalon> UserSalons { get; set; } = new List<UserSalon>();
}
