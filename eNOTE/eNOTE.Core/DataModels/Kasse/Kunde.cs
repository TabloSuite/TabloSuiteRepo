namespace ENote.Core.DataModels.Kasse;

/// <summary>
/// Kunde eines Ladens (Kundenkartei). Optional am Verkauf; die Kasse funktioniert ohne Kunde weiter.
/// Die Telefonnummer wird nur gespeichert, wenn der Kunde zustimmt (<see cref="TelefonEinwilligungUtc"/>).
/// </summary>
public class Kunde
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Wie eingegeben (für die Anzeige), nur mit Einwilligung.</summary>
    public string? Telefon { get; set; }
    /// <summary>Nur Ziffern (Suche und Dubletten-Prüfung).</summary>
    public string? TelefonZiffern { get; set; }
    /// <summary>Zeitpunkt, zu dem der Kunde der Speicherung seiner Nummer zugestimmt hat.</summary>
    public DateTime? TelefonEinwilligungUtc { get; set; }

    /// <summary>Freie Notiz des Ladens (z. B. Allergie, Wunsch-Friseurin).</summary>
    public string? Notiz { get; set; }

    public DateTime ErstelltUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;
    public Guid? ErstelltVonId { get; set; }
    public DateTime? GeaendertUtc { get; set; }
}
