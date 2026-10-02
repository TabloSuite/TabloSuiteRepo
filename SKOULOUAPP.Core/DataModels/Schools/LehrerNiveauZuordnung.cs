using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Identity;
namespace SkoulouApp.Core.DataModels.Schools;

/// <summary>
/// Ordnet einen Lehrer einer konkreten Klasse innerhalb einer
/// konkreten Schule zu. Ein Lehrer kann mehreren Klassen mehrerer Schulen des
/// Direktors zugeordnet werden. Ohne Zuordnung darf ein Lehrer keine Schüler verwalten.
/// </summary>
public class LehrerNiveauZuordnung : AuditableModel
{
    public Guid LehrerID { get; set; }
    public AppUser Lehrer { get; set; } = null!;

    public Guid SchulID { get; set; }
    public School Schule { get; set; } = null!;

    /// <summary>
    /// <summary>Die konkrete Klasse, für die der Lehrer verantwortlich ist.</summary>
    public Guid KlasseId { get; set; }
    public Courses.Klasse Klasse { get; set; } = null!;

    /// <summary>Verbindliche Unterrichtsstunden pro Woche für die Provisionsverteilung.</summary>
    public decimal Wochenstunden { get; set; } = 1m;
}
