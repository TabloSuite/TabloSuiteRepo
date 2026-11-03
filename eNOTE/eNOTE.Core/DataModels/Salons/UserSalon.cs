using ENote.Core.DataModels.Identity;

namespace ENote.Core.DataModels.Salons;

/// <summary>
/// Zuordnung eines Benutzers zu einem Salon inklusive seiner salonbezogenen
/// Rechte und der Kassen-PIN für den schnellen Kassenstart am Tablet.
/// </summary>
public class UserSalon
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AppUserId { get; set; }
    public AppUser AppUser { get; set; } = null!;
    public Guid SalonId { get; set; }
    public Salon Salon { get; set; } = null!;

    /// <summary>Gehashte kurze Kassen-PIN (nur Kassierer).</summary>
    public string? KassenPinHash { get; set; }

    /// <summary>Anzahl aufeinanderfolgender falscher PIN-Eingaben (Sperrlogik).</summary>
    public int PinFehlversuche { get; set; }
    public DateTime? PinGesperrtBisUtc { get; set; }

    // Vom Besitzer delegierbare Rechte
    public bool KannFinanzenSehen { get; set; }
    public bool KannLeistungenVerwalten { get; set; }
    public bool KannRabatteVerwalten { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
