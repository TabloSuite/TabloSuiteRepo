namespace SkoulouApp.Core.Models.Student;

public class LessonDetailViewModel
{
    public Guid LessonId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<LessonContentPageViewModel> ContentPages { get; set; } = [];
    public string CourseCode { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime? ExerciseSubmissionDueAtUtc { get; set; }
    public bool IsExerciseReviewMode { get; set; }
    public List<LessonMediaViewModel> Media { get; set; } = [];
    public List<ExerciseViewModel> Exercises { get; set; } = [];
}

public class LessonContentPageViewModel
{
    public int PageNumber { get; set; }
    public string ContentHtml { get; set; } = string.Empty;
}

public class LessonMediaViewModel
{
    public string MediaType { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string AltText { get; set; } = string.Empty;
}

public class ExerciseViewModel
{
    public Guid ExerciseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public int Order { get; set; }
    public int MaxPoints { get; set; }
    public int? AwardedPoints { get; set; }
    public bool IsAnsweredCorrectly { get; set; }
    public Guid? SelectedOptionId { get; set; }
    public List<Guid> SubmittedSelectedOptionIds { get; set; } = [];
    public List<Guid> CorrectOptionIds { get; set; } = [];
    public bool HasSubmittedAttempt { get; set; }

    /// <summary>
    /// Aufgabentyp: bestimmt das Eingabewidget. Werte: "SingleChoice", "MultipleChoice", "Text", "GapFill", "SentenceBuilding", "AudioText", "AudioSingleChoice", "AudioMultipleChoice".
    /// Leerer/unbekannter Wert wird als "Choice" behandelt (Rückwärtskompatibilität).
    /// </summary>
    public string ExerciseType { get; set; } = string.Empty;

    /// <summary>Bei Audio-Aufgaben: vom TTS vorzulesender Text.</summary>
    public string? AudioText { get; set; }

    /// <summary>Sprachcode für TTS (de-DE, fr-FR, en-US).</summary>
    public string TtsLanguage { get; set; } = "de-DE";

    /// <summary>Anzahl der Lücken bei GapFill-Aufgaben.</summary>
    public int GapCount { get; set; }

    /// <summary>True, wenn für diesen Aufgabentyp eine automatische Bewertung möglich ist (Choice oder Musterlösung vorhanden).</summary>
    public bool HasAutomaticGrading { get; set; }

    public bool IsPendingManualGrading { get; set; }
    public bool IsManuallyReviewed { get; set; }
    public bool? ManualIsCorrect { get; set; }
    public string? ManualFeedback { get; set; }

    public List<string> AcceptedAnswers { get; set; } = [];
    public List<List<string>> CorrectGapAnswers { get; set; } = [];

    /// <summary>Letzte Freitext-/Satzbau-Antwort des Schülers.</summary>
    public string? SubmittedTextAnswer { get; set; }

    /// <summary>Letzte eingegebene Lückenwerte.</summary>
    public List<string> SubmittedGapValues { get; set; } = [];

    public List<ExerciseOptionViewModel> Options { get; set; } = [];
}

public class ExerciseOptionViewModel
{
    public Guid OptionId { get; set; }
    public string Text { get; set; } = string.Empty;
    public int Order { get; set; }
}
