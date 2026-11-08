namespace ENote.Core.DataModels.Katalog;

/// <summary>Eine vom Besitzer verwaltete Kategorie für Leistungen (z. B. „Tissus“, „Boissons“).</summary>
public class LeistungKategorie
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public Salons.Salon Salon { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public bool IstAktiv { get; set; } = true;

    /// <summary>Optionale übergeordnete Kategorie (null = Hauptkategorie). Max. 1 Unterebene.</summary>
    public Guid? ParentId { get; set; }
    public LeistungKategorie? Parent { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
