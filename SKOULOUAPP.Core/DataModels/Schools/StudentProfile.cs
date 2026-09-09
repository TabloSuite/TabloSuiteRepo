using HalloApp.Core.DataModels.BaseModel;
using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
namespace HalloApp.Core.DataModels.Schools;

public class StudentProfile : AuditableModel
{
    public Guid AppUserId { get; set; }
    public AppUser AppUser { get; set; } = null!;

    public Guid SchoolId { get; set; }
    public School School { get; set; } = null!;

    public string CurrentLevelCode { get; set; } = string.Empty;

    /// <summary>
    /// Eindeutige aktuelle Klasse des Schülers. Der Anzeige-Code bleibt für UI und
    /// bestehende Texte erhalten, darf aber nicht für Berechtigungsprüfungen dienen.
    /// </summary>
    public Guid? CurrentKlasseId { get; set; }
    public Klasse? CurrentKlasse { get; set; }

    public int CompletedLessonsCount { get; set; }

    /// <summary>
    /// Kennzeichnet Schüler, die die Schule verlassen haben.
    /// Archivierte Schüler bleiben historisch erhalten, werden aber aus aktiven Listen ausgeblendet.
    /// </summary>
    public bool IsArchived { get; set; }

    /// <summary>Optionale Telefonnummer eines Elternteils bzw. Erziehungsberechtigten.</summary>
    public string? ElternTelefonNummer { get; set; }

    /// <summary>Optionale E-Mail-Adresse eines Elternteils bzw. Erziehungsberechtigten.</summary>
    public string? ElternEmail { get; set; }
}







