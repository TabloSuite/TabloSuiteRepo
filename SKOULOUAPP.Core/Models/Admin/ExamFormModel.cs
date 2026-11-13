using System.ComponentModel.DataAnnotations;
using SkoulouApp.Core.DataModels.Exams;

namespace SkoulouApp.Core.Models.Admin;

/// <summary>
/// Formmodell zum Erstellen/Bearbeiten einer Prüfung (Exam) mit gruppierten
/// Aufgabenblöcken (Exercices) und Aufgaben (Questions).
/// </summary>
public class ExamFormModel
{
    public Guid? ExamId { get; set; }

    [Required(ErrorMessage = "Veuillez saisir un titre.")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Veuillez choisir une classe.")]
    public string NiveauCode { get; set; } = string.Empty;

    public Guid? KlasseId { get; set; }

    [Range(1, 600, ErrorMessage = "Durée entre 1 et 600 minutes.")]
    public int DurationInMinutes { get; set; } = 60;

    public DateTime? SubmissionDueAtUtc { get; set; }

    [Range(0, 100, ErrorMessage = "Score de réussite entre 0 et 100.")]
    public int PassingScore { get; set; } = 50;

    /// <summary>Bestehender Examtyp; Schülerexams zählen nur zum Score.</summary>
    public ExamType ExamType { get; set; } = ExamType.TestExam;

    public ExamReleaseTarget ReleaseTarget { get; set; } = ExamReleaseTarget.PrivateAuthor;

    public bool ShareWithSchoolDirection { get; set; }

    public bool AssignToClassStudents { get; set; }

    public bool IsPdfExportEnabled { get; set; }

    /// <summary>
    /// Nur zur Anzeige (« Saisi par la direction »): wird beim Laden gesetzt und beim
    /// Speichern ignoriert – die Kennzeichnung bestimmt ausschließlich der Server.
    /// </summary>
    public bool IstVonDirektionErfasst { get; set; }

    public List<ExamExerciseFormModel> Exercises { get; set; } = [];
}

/// <summary>Aufgabenblock (Exercice) innerhalb einer Prüfung.</summary>
public class ExamExerciseFormModel
{
    public Guid? ExerciseId { get; set; }

    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Aufgabenstellung des Blocks (z. B. "Choisissez les vraies réponses").</summary>
    public string Consigne { get; set; } = string.Empty;

    public List<ExamQuestionFormModel> Questions { get; set; } = [];
}

/// <summary>Einzelne Prüfungsaufgabe.</summary>
public class ExamQuestionFormModel
{
    public Guid? QuestionId { get; set; }

    [Required(ErrorMessage = "Veuillez saisir l'énoncé de la question.")]
    public string Prompt { get; set; } = string.Empty;

    public ExamQuestionType QuestionType { get; set; } = ExamQuestionType.SingleChoice;

    [Range(1, 100)]
    public int Points { get; set; } = 1;

    /// <summary>Bei Audio-Fragen (AudioText/AudioChoice): der vorzulesende Text.</summary>
    public string? AudioText { get; set; }

    /// <summary>Sprachcode für die Audioausgabe dieser Frage (de-DE, fr-FR, en-US).</summary>
    public string TtsLanguage { get; set; } = "de-DE";

    /// <summary>
    /// Optionale Musterlösungen für Text-basierte Fragen (FreeText, SentenceBuilding, AudioText).
    /// Eine Variante pro Zeile; leer = manuelle Benotung.
    /// </summary>
    public string? AcceptedAnswers { get; set; }

    /// <summary>Bei GapFill: die Lücken mit ihren akzeptierten Varianten (Reihenfolge = Marker [[1]], [[2]] ...).</summary>
    public List<ExamGapFormModel> Gaps { get; set; } = [];

    public List<ExamQuestionOptionFormModel> Options { get; set; } = [];
}

/// <summary>Eine Lücke eines GapFill-Aufgabentyps.</summary>
public class ExamGapFormModel
{
    /// <summary>Akzeptierte Varianten für diese Lücke, eine pro Zeile.</summary>
    public string AcceptedAnswers { get; set; } = string.Empty;
}

/// <summary>Antwortoption einer Auswahl-Aufgabe.</summary>
public class ExamQuestionOptionFormModel
{
    public Guid? OptionId { get; set; }

    [Required(ErrorMessage = "Veuillez saisir le texte de l'option.")]
    public string Text { get; set; } = string.Empty;

    public bool IsCorrect { get; set; }
}
