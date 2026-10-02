using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;

namespace SkoulouApp.Core.DataModels.Schools;

/// <summary>
/// Eingefrorener ProduktOwner-Provisionsanteil einer abgerechneten Freischaltung.
/// Der Betrag entspricht dem zum Zeitpunkt der Abrechnung gültigen Schulleiter-Anteil.
/// </summary>
public class ProduktOwnerProvisionsAuszahlung : AuditableModel
{
    public Guid AbrechnungsperiodeId { get; set; }
    public Abrechnungsperiode Abrechnungsperiode { get; set; } = null!;

    public Guid FreischaltungId { get; set; }
    public LevelEnrollment Freischaltung { get; set; } = null!;

    public Guid ProduktOwnerId { get; set; }
    public AppUser ProduktOwner { get; set; } = null!;

    public decimal Betrag { get; set; }
    public DateTime? AlsErhaltenBestaetigtAmUtc { get; set; }
}