namespace ENote.Core.DataModels.Katalog;

/// <summary>Eine vom Besitzer verwaltete Kategorie für Leistungen (z. B. „Tissus“, „Boissons“).</summary>
public class LeistungKategorie
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public Salons.Salon Salon { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public bool IstAktiv { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
