using SkoulouApp.Core.DataModels.Identity;
namespace SkoulouApp.Core.DataModels.Schools;

/// <summary>
/// Zuordnung eines Kontos zu einer Schule. Regel seit 10.11.2026: Ein Konto der Rollen
/// SchulLeiter, Koordinator, Lehrer und Schüler gehört zu genau einer Schule
/// (eindeutiger Index auf <see cref="AppUserId"/> für alle Zeilen ohne Plattformzuordnung).
/// Nur ProduktOwner betreuen mehrere Schulen; ihre Zeilen tragen <see cref="IstPlattformZuordnung"/>.
/// </summary>
public class UserSchool
{
    public Guid AppUserId { get; set; }
    public AppUser AppUser { get; set; } = null!;

    public Guid SchoolId { get; set; }
    public School School { get; set; } = null!;

    /// <summary>Betreuung durch einen ProduktOwner (Plattformrolle), keine Schulmitgliedschaft.</summary>
    public bool IstPlattformZuordnung { get; set; }

    public bool CanManageStudents { get; set; }
    public bool CanAccessFinances { get; set; }
    public bool CanViewFinanceDashboard { get; set; }
    public bool CanCreateFinanceEntries { get; set; }
    public bool CanManageCyclePayments { get; set; }
    public bool CanAccessBilling { get; set; }
}
