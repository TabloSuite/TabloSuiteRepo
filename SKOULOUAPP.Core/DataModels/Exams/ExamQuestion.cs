using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
namespace SkoulouApp.Core.DataModels.Exams;

public class ExamQuestion : AuditableModel
{
    public Guid ExamId { get; set; }
    public Exam Exam { get; set; } = null!;

    /// <summary>
    /// Optionaler Aufgabenblock, dem diese Aufgabe zugeordnet ist.
    /// Nullable für Rückwärtskompatibilität mit flach angelegten Exams (Seed/Alt-Daten).
    /// </summary>
    public Guid? ExamExerciseId { get; set; }
    public ExamExercise? ExamExercise { get; set; }

    public string Prompt { get; set; } = string.Empty;
    public int Order { get; set; }

    public ExamQuestionType QuestionType { get; set; }
    public int Points { get; set; } = 1;

    /// <summary>
    /// Bei Audio-Fragen (AudioText/AudioChoice): der vom TTS-Provider vorzulesende Text.
    /// </summary>
    public string? AudioText { get; set; }

    /// <summary>
    /// Sprachcode für die Audio-Ausgabe (z. B. de-DE, fr-FR, en-US).
    /// </summary>
    public string TtsLanguage { get; set; } = "de-DE";

    /// <summary>
    /// Optionale Musterlösungen für Text-basierte Fragen (FreeText, SentenceBuilding, AudioText).
    /// JSON-Array akzeptierter Varianten. Gesetzt = automatische tolerante Bewertung, sonst manuell.
    /// </summary>
    public string? AcceptedAnswersJson { get; set; }

    /// <summary>
    /// Bei GapFill: JSON-Definition der Lücken (Array pro Lücke mit akzeptierten Varianten).
    /// </summary>
    public string? GapAnswersJson { get; set; }

    public ICollection<ExamQuestionOption> Options { get; set; } = new List<ExamQuestionOption>();
}







