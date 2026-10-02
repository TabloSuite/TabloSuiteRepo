using SkoulouApp.Core.Models.TeacherLessons;

namespace SkoulouApp.Core.Models.Student;

public class StudentClassHistoryItem
{
    public Guid DurchlaufId { get; set; }
    public DateTime StartDatum { get; set; }
    public DateTime? EndDatum { get; set; }
    public Guid KlasseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public int? ScorePercent { get; set; }
    public int ScoreCount { get; set; }
    public string? AbschlussNoteText { get; set; }
    public string? MentionText { get; set; }
    public bool? IsBestanden { get; set; }
    public int AbschlussExamCount { get; set; }
    public List<TeacherLessonListItem> TeacherLessons { get; set; } = [];
}
