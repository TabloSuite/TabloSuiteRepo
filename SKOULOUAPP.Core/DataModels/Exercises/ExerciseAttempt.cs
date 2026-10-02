using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
namespace SkoulouApp.Core.DataModels.Exercises;

public class ExerciseAttempt : AuditableModel
{
    public Guid ExerciseId { get; set; }
    public Exercise Exercise { get; set; } = null!;

    public Guid StudentId { get; set; }
    public AppUser Student { get; set; } = null!;

    public Guid? SelectedOptionId { get; set; }

    /// <summary>
    /// Bei MultipleChoice-/AudioMultipleChoice-Übungen: JSON-Array der gewählten Options-Ids.
    /// </summary>
    public string? SelectedOptionIdsJson { get; set; }

    /// <summary>
    /// Bei Text-/Satzbau-/Audio-Text-Übungen: die Freitextantwort des Schülers.
    /// </summary>
    public string? TextAnswer { get; set; }

    /// <summary>
    /// Bei Lückentext-Übungen: JSON-Array der eingegebenen Lückenwerte (Reihenfolge = Marker).
    /// </summary>
    public string? GapAnswersJson { get; set; }

    public bool IsCorrect { get; set; }

    /// <summary>
    /// Zuweisungsrunde der Lektion zum Zeitpunkt der Abgabe.
    /// </summary>
    public int AssignmentVersion { get; set; } = 1;

    /// <summary>
    /// Kompakter Snapshot der Aufgabenfrage zum Abgabezeitpunkt.
    /// </summary>
    public string? ExerciseSnapshotJson { get; set; }

    /// <summary>
    /// True, wenn die Antwort mangels Musterlösung manuell benotet werden muss und noch nicht bewertet wurde.
    /// </summary>
    public bool RequiresManualGrading { get; set; }

    public bool? ManuallyGradedIsCorrect { get; set; }
    public string? ManualFeedback { get; set; }
    public int AwardedPoints { get; set; }
    public Guid? ManuallyGradedByUserId { get; set; }
    public DateTime? ManuallyGradedAtUtc { get; set; }

    public DateTime AnsweredAtUtc { get; set; } = DateTime.UtcNow;
}







