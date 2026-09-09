namespace HalloApp.Core.Models.Student;

public class StudentCourseProgressItem
{
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public int LessonCount { get; set; }
    public int ExerciseCount { get; set; }
    public int ExamCount { get; set; }
    public bool IsCurrentLevel { get; set; }
    public bool IsUnlocked { get; set; }
}
