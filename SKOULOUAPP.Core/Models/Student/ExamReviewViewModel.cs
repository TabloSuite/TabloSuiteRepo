namespace SkoulouApp.Core.Models.Student;

public class ExamReviewViewModel
{
    public Guid AttemptId { get; set; }
    public Guid ExamId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string CourseCode { get; set; } = string.Empty;
    public int ScorePercent { get; set; }
    public int PassingScore { get; set; }
    public bool IsPassed { get; set; }
    public bool RequiresManualGrading { get; set; }
    public List<ExamQuestionReviewViewModel> Questions { get; set; } = [];
}

public class ExamQuestionReviewViewModel
{
    public Guid QuestionId { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public int Order { get; set; }
    public string QuestionType { get; set; } = string.Empty;
    public string? AudioText { get; set; }
    public string TtsLanguage { get; set; } = "de-DE";
    public int Points { get; set; }
    public bool HasAnswer { get; set; }
    public bool IsCorrect { get; set; }
    public bool IsManuallyGraded { get; set; }
    public int? ManualPoints { get; set; }
    public Guid? SelectedOptionId { get; set; }
    public List<Guid> SelectedOptionIds { get; set; } = [];
    public string? TextAnswer { get; set; }
    public List<string> GapAnswers { get; set; } = [];
    public List<string> AcceptedAnswers { get; set; } = [];
    public List<List<string>> CorrectGapAnswers { get; set; } = [];
    public List<ExamReviewOptionViewModel> Options { get; set; } = [];
}

public class ExamReviewOptionViewModel
{
    public Guid OptionId { get; set; }
    public string Text { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsCorrect { get; set; }
}
