namespace HalloApp.Core.Models.TeacherLessons;

public class PendingManualExerciseReviewItem
{
    public Guid AttemptId { get; set; }
    public Guid LessonId { get; set; }
    public string LessonTitle { get; set; } = string.Empty;
    public Guid ExerciseId { get; set; }
    public string ExerciseTitle { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public int ExercisePoints { get; set; }
    public Guid StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string? SubmittedTextAnswer { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
}