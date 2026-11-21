namespace ENote.Core.DataModels.Abo;

public enum AuszahlungStatus
{
    Offen = 0,
    Bestaetigt = 1
}

/// <summary>
/// Monatliche Provisions-Auszahlung an einen ProduktOwner, berechnet aus den
/// bestätigten Abo-Einnahmen seiner zugewiesenen Läden. Der SuperAdmin bestätigt.
/// </summary>
public class ProduktOwnerAuszahlung
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProduktOwnerId { get; set; }
    public Identity.AppUser ProduktOwner { get; set; } = null!;

    public int Jahr { get; set; }
    public int Monat { get; set; }

    /// <summary>Berechneter Provisionsbetrag (Summe über alle Läden).</summary>
    public decimal Betrag { get; set; }

    /// <summary>Nachvollziehbare Aufschlüsselung je Laden (JSON oder Text).</summary>
    public string? Aufschluesselung { get; set; }

    public AuszahlungStatus Status { get; set; } = AuszahlungStatus.Offen;
    public DateTime? BestaetigtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;
}
