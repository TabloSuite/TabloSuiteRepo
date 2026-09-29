using HalloApp.Core.DataModels.BaseModel;
using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
namespace HalloApp.Core.DataModels.Lessons;

public class Lesson : AuditableModel
{
    public Guid KlasseId { get; set; }
    public Klasse Klasse { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int Order { get; set; }

    /// <summary>
    /// Gemeinsame Freigabe für Lektion + Aufgaben. Erst bei true dürfen Schüler
    /// auf Inhalte und Exercises zugreifen.
    /// </summary>
    public bool IsPublished { get; set; } = true;

    /// <summary>
    /// Frist für die Abgabe manueller/offener Aufgaben.
    /// Nach Ablauf sind neue offene Abgaben gesperrt.
    /// </summary>
    public DateTime? ExerciseSubmissionDueAtUtc { get; set; }

    /// <summary>
    /// Versionsrunde der Aufgaben-Zuweisung. Wird nach Fristablauf erhöht,
    /// damit neue Änderungen nur für neue Abgaben gelten.
    /// </summary>
    public int AssignmentVersion { get; set; } = 1;

    public Guid? FachId { get; set; }
    public Fach? Fach { get; set; }

    public Guid? CreatedByTeacherId { get; set; }
    public AppUser? CreatedByTeacher { get; set; }

    public ICollection<LessonMedia> MediaItems { get; set; } = new List<LessonMedia>();
    public ICollection<Exercise> Exercises { get; set; } = new List<Exercise>();
}







