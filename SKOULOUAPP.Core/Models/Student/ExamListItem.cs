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
    public bool HasActiveAttempt { get; set; }
    public bool CanDownloadPdf { get; set; }
    public DateTime? FirstDeadlineUtc { get; set; }
    public DateTime? SubmissionDueAtUtc { get; set; }
    public bool IsSuperseded { get; set; }
    public bool CanStart => QuestionCount > 0 && !IsSuperseded && !HasAttempt
        && (!FirstDeadlineUtc.HasValue || FirstDeadlineUtc.Value > DateTime.UtcNow)
        && (!SubmissionDueAtUtc.HasValue || SubmissionDueAtUtc.Value > DateTime.UtcNow);
    public bool IsPassed { get; set; }
    public int BestScorePercent { get; set; }
}
