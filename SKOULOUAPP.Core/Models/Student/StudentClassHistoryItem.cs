using HalloApp.Core.Models.TeacherLessons;

namespace HalloApp.Core.Models.Student;

public class StudentClassHistoryItem
{
    public Guid KlasseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public int? ScorePercent { get; set; }
    public int ScoreCount { get; set; }
    public string? AbschlussNoteText { get; set; }
    public int AbschlussExamCount { get; set; }
    public List<TeacherLessonListItem> TeacherLessons { get; set; } = [];
}
