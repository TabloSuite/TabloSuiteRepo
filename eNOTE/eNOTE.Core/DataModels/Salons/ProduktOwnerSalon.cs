using ENote.Core.DataModels.Identity;

namespace ENote.Core.DataModels.Salons;

/// <summary>
/// Zuweisung eines Ladens an einen ProduktOwner durch den SuperAdmin.
/// Bestimmt, welche Läden der PO verwaltet, und steuert seine Umsatz-Sichtbarkeit.
/// </summary>
public class ProduktOwnerSalon
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProduktOwnerId { get; set; }
    public AppUser ProduktOwner { get; set; } = null!;
    public Guid SalonId { get; set; }
    public Salon Salon { get; set; } = null!;

    /// <summary>PO darf den Umsatz dieses Ladens sehen.</summary>
    public bool KannLadenUmsatzSehen { get; set; } = true;

    /// <summary>PO darf den Plattform-/Abo-Umsatz dieses Ladens sehen (sensibel).</summary>
    public bool KannPlattformUmsatzSehen { get; set; }

    /// <summary>true, wenn der PO den Laden selbst angelegt hat (automatisch zugewiesen).</summary>
    public bool SelbstAngelegt { get; set; }

    /// <summary>Vom SuperAdmin festgelegte Provision des PO für diesen Laden (0-100 %).</summary>
    public decimal? ProzentProvision { get; set; }

    /// <summary>Vom SuperAdmin festgelegte feste Provision des PO für diesen Laden (pro Abo).</summary>
    public decimal? FestbetragProvision { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
