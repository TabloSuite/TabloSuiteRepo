namespace HalloApp.Core.Models.Student;

public class ExamListItem
{
    public Guid ExamId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CourseCode { get; set; } = string.Empty;
    public int DurationInMinutes { get; set; }
    public int PassingScore { get; set; }
    public int QuestionCount { get; set; }

    public bool HasAttempt { get; set; }
    public bool IsPassed { get; set; }
    public int BestScorePercent { get; set; }
}
