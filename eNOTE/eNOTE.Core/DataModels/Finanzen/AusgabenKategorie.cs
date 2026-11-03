namespace ENote.Core.DataModels.Finanzen;

/// <summary>Vom Besitzer festgelegte Ausgaben-Kategorie (z. B. „Lohn“, „Strom“, „Wareneinkauf“).</summary>
public class AusgabenKategorie
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public Salons.Salon Salon { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public bool IstAktiv { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
