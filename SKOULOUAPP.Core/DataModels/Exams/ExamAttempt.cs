using HalloApp.Core.DataModels.BaseModel;
using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
namespace HalloApp.Core.DataModels.Exams;

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







