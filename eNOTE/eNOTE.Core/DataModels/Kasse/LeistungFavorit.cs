namespace ENote.Core.DataModels.Kasse;

/// <summary>
/// Eine als Favorit markierte Leistung eines Ladens. Favoriten sind app-weit
/// (pro Salon) und werden in der Kasse für alle zuerst angezeigt. Kassierer
/// und Besitzer können sie verwalten.
/// </summary>
public class LeistungFavorit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public Guid LeistungId { get; set; }
    public Katalog.Leistung Leistung { get; set; } = null!;

    /// <summary>Wer den Favoriten gesetzt hat (Kassierer oder Besitzer).</summary>
    public Guid GesetztVonId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
