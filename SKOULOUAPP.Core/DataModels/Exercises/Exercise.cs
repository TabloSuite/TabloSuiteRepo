using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
namespace SkoulouApp.Core.DataModels.Exercises;

public class Exercise : AuditableModel
{
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string ExerciseType { get; set; } = string.Empty;
    public int Order { get; set; }

    public string Question { get; set; } = string.Empty;
    public int Points { get; set; } = 10;

    /// <summary>
    /// Bei Audio-Übungen: der vom TTS-Provider vorzulesende Text.
    /// </summary>
    public string? AudioText { get; set; }

    /// <summary>
    /// Sprachcode für die Audio-Ausgabe (z. B. de-DE, fr-FR, en-US).
    /// </summary>
    public string TtsLanguage { get; set; } = "de-DE";

    /// <summary>
    /// Optionale Musterlösungen für Text-basierte Übungen (Freitext, Satzbau, Audio-Text).
    /// JSON-Array akzeptierter Varianten. Gesetzt = automatische tolerante Bewertung, sonst manuell.
    /// </summary>
    public string? AcceptedAnswersJson { get; set; }

    /// <summary>
    /// Bei Lückentext-Übungen: JSON-Definition der Lücken (Array pro Lücke mit akzeptierten Varianten).
    /// </summary>
    public string? GapAnswersJson { get; set; }

    public ICollection<ExerciseOption> Options { get; set; } = new List<ExerciseOption>();
    public ICollection<ExerciseAttempt> Attempts { get; set; } = new List<ExerciseAttempt>();
}







