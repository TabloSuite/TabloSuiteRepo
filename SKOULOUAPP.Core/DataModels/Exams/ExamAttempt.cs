using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
namespace SkoulouApp.Core.DataModels.Exams;

public class ExamAttempt : AuditableModel
{
    public Guid ExamId { get; set; }
    public Exam Exam { get; set; } = null!;

    public Guid StudentId { get; set; }
    public AppUser Student { get; set; } = null!;

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime DeadlineUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }

    public int CorrectAnswers { get; set; }
    public int TotalQuestions { get; set; }
    public int ScorePercent { get; set; }
    public bool IsPassed { get; set; }
    public bool IsCompleted { get; set; }

    /// <summary>
    /// Von Lehrer oder Schulleiter nachgetragenes Ergebnis. Bei einem abschlussrelevanten
    /// Exam hat diese Note Vorrang vor der automatisch ermittelten Prozentzahl.
    /// </summary>
    public decimal? ManuelleNote { get; set; }

    public DateTime? ManuelleNoteErfasstAmUtc { get; set; }
    public Guid? ManuelleNoteErfasstVonUserId { get; set; }

    /// <summary>
    /// True, solange mindestens eine Text-Antwort ohne Musterlösung noch manuell benotet werden muss.
    /// </summary>
    public bool RequiresManualGrading { get; set; }

    public ICollection<ExamAnswer> Answers { get; set; } = new List<ExamAnswer>();
}







