namespace ENote.Core.DataModels.Salons;

/// <summary>
/// Vom Besitzer akzeptierter Mobile-Money-Anbieter eines Ladens. Ohne Einträge gelten
/// alle aktiven Anbieter des Laden-Landes.
/// </summary>
public class SalonMobileMoneyAnbieter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public Salon Salon { get; set; } = null!;
    public Guid AnbieterId { get; set; }
    public Kasse.MobileMoneyAnbieter Anbieter { get; set; } = null!;
}
