namespace SkoulouApp.Core.Models.Student;

public class ExamSessionViewModel
{
    public Guid AttemptId { get; set; }
    public Guid ExamId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CourseCode { get; set; } = string.Empty;
    public int DurationInMinutes { get; set; }
    public int PassingScore { get; set; }
    public DateTime DeadlineUtc { get; set; }
    public bool IsResumed { get; set; }
    public List<ExamAnswerSubmission> SavedAnswers { get; set; } = [];
    public List<ExamQuestionViewModel> Questions { get; set; } = [];
}

public class ExamQuestionViewModel
{
    public Guid QuestionId { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public int Order { get; set; }

    /// <summary>Aufgabentyp als String: "SingleChoice", "MultipleChoice", "AudioSingleChoice", "AudioMultipleChoice", "GapFill", "Text", "SentenceBuilding", "AudioText".</summary>
    public string QuestionType { get; set; } = "Choice";

    /// <summary>Bei Audio-Fragen: vom TTS vorzulesender Text.</summary>
    public string? AudioText { get; set; }

    /// <summary>Sprachcode für TTS (de-DE, fr-FR, en-US).</summary>
    public string TtsLanguage { get; set; } = "de-DE";

    /// <summary>Anzahl der Lücken bei GapFill.</summary>
    public int GapCount { get; set; }

    public int Points { get; set; } = 1;

    public List<ExamOptionViewModel> Options { get; set; } = [];
}

public class ExamOptionViewModel
{
    public Guid OptionId { get; set; }
    public string Text { get; set; } = string.Empty;
    public int Order { get; set; }
}
