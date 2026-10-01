namespace HalloApp.Core.Models.Student;

public class LessonListItem
{
    public Guid LessonId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string FachName { get; set; } = string.Empty;
    public Guid? TeacherId { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public IReadOnlyList<Guid> FilterTeacherIds { get; set; } = [];
    public IReadOnlyList<(Guid Id, string Name)> FilterTeachers { get; set; } = [];
    public int Order { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public int ExerciseCount { get; set; }
    public int CorrectExerciseCount { get; set; }
    public int ExercisePointsEarned { get; set; }
    public int ExercisePointsTotal { get; set; }
    public DateTime? ExerciseSubmissionDueAtUtc { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsUnlocked { get; set; } = true;

    public int ProgressPercent => ExerciseCount == 0
        ? 0
        : (int)Math.Round(CorrectExerciseCount * 100.0 / ExerciseCount);
}
