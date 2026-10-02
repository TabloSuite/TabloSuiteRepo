using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels.Exams;

/// <summary>
/// Speichert die Antwort eines Schülers auf eine einzelne Prüfungsfrage.
/// Notwendig für Text-/Lückentext-Fragen und die manuelle Benotung ohne Musterlösung.
/// </summary>
public class ExamAnswer : AuditableModel
{
    public Guid ExamAttemptId { get; set; }
    public ExamAttempt ExamAttempt { get; set; } = null!;

    public Guid ExamQuestionId { get; set; }
    public ExamQuestion ExamQuestion { get; set; } = null!;

    /// <summary>Bei Choice-/AudioSingleChoice-Fragen: gewählte Option (Einzelauswahl).</summary>
    public Guid? SelectedOptionId { get; set; }

    /// <summary>Bei MultipleChoice-/AudioMultipleChoice-Fragen: JSON-Array der gewählten Options-Ids.</summary>
    public string? SelectedOptionIdsJson { get; set; }

    /// <summary>Bei Text-/Satzbau-/Audio-Text-Fragen: die Freitextantwort.</summary>
    public string? FreeTextAnswer { get; set; }

    /// <summary>Bei GapFill: JSON-Array der eingegebenen Lückenwerte (Reihenfolge = Marker).</summary>
    public string? GapAnswersJson { get; set; }

    public bool IsCorrect { get; set; }

    /// <summary>True, wenn die Antwort mangels Musterlösung manuell benotet werden muss.</summary>
    public bool IsManuallyGraded { get; set; }

    /// <summary>Von der Lehrkraft vergebene Punkte bei manueller Benotung.</summary>
    public int? ManualPoints { get; set; }
}
