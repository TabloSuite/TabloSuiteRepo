namespace HalloApp.Core.DataModels.BaseModel;

public abstract class AuditableModel
{
    public Guid Id { get; set; } // Eindeutige Kennung des Modells.
    public DateTime ErstelltAmUtc { get; set; } = DateTime.UtcNow; // Zeitpunkt, zu dem das Modell erstellt wurde.
    public DateTime? GeaendertAmUtc { get; set; } // Zeitpunkt, zu dem das Modell zuletzt geändert wurde.
}



