using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
namespace SkoulouApp.Core.DataModels.Exams;

public class Exam : AuditableModel
{
    public Guid KlasseId { get; set; }
    public Klasse Klasse { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DurationInMinutes { get; set; }
    public DateTime? SubmissionDueAtUtc { get; set; }
    public bool IsSuperseded { get; set; }
    public int PassingScore { get; set; }
    public ExamType ExamType { get; set; } = ExamType.TestExam;

    /// <summary>Lehrer, der diese schuleigene Prüfung erstellt hat.</summary>
    public Guid? AuthorId { get; set; }
    public AppUser? Author { get; set; }

    /// <summary>Gibt an, ob die Prüfung für Schüler der zugeordneten Klasse sichtbar ist.</summary>
    public bool IsPublishedToStudents { get; set; }

    /// <summary>Gibt an, ob der Autor die Prüfung mit dem Schulleiter teilt.</summary>
    public bool IsSharedWithSchool { get; set; }

    /// <summary>Gibt an, ob der Autor den PDF-Export seiner Prüfung erlaubt.</summary>
    public bool IsPdfExportEnabled { get; set; }

    public ICollection<ExamQuestion> Questions { get; set; } = new List<ExamQuestion>();
    public ICollection<ExamExercise> Exercises { get; set; } = new List<ExamExercise>();
    public ICollection<ExamAttempt> Attempts { get; set; } = new List<ExamAttempt>();
}







