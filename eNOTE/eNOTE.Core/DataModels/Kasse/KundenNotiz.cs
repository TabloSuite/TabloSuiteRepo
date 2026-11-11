namespace ENote.Core.DataModels.Kasse;

/// <summary>
/// Notiz zu einem Kunden (Verlauf, neueste zuerst), optional einem Besuch zugeordnet.
/// Für Besitzer und Mitarbeitende des Ladens sichtbar – nur das Nötige notieren.
/// </summary>
public class KundenNotiz
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public Guid KundeId { get; set; }
    public Kunde Kunde { get; set; } = null!;
    public Guid? BesuchId { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime ErstelltUtc { get; set; } = DateTime.UtcNow;
    public Guid? ErstelltVonId { get; set; }
    /// <summary>Name zum Zeitpunkt der Notiz (bleibt lesbar, auch wenn das Konto später gelöscht wird).</summary>
    public string ErstelltVonName { get; set; } = string.Empty;
}
