using HalloApp.Core.DataModels.BaseModel;
using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Identity;

namespace HalloApp.Core.DataModels.Schools;

/// <summary>
/// Eingefrorener Lehrer-Provisionsanteil einer abgerechneten Freischaltung.
/// Die Berechnung basiert auf den Wochenstunden zum Zeitpunkt der Rechnungserstellung.
/// </summary>
public class LehrerProvisionsAuszahlung : AuditableModel
{
    public Guid AbrechnungsperiodeId { get; set; }
    public Abrechnungsperiode Abrechnungsperiode { get; set; } = null!;

    public Guid FreischaltungId { get; set; }
    public LevelEnrollment Freischaltung { get; set; } = null!;

    public Guid LehrerId { get; set; }
    public AppUser Lehrer { get; set; } = null!;

    public decimal WochenstundenSnapshot { get; set; }
    public decimal GesamtWochenstundenSnapshot { get; set; }
    public decimal Betrag { get; set; }

    public DateTime? AlsErhaltenBestaetigtAmUtc { get; set; }
}
