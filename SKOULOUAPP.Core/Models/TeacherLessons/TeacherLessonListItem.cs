namespace HalloApp.Core.Models.TeacherLessons;

/// <summary>
/// Übersichtseintrag für ein Kursskript – für Lehrer-Verwaltung und Schülerliste.
/// </summary>
public class TeacherLessonListItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public Guid? FachId { get; set; }
    public string? FachName { get; set; }
    public string? CourseTheme { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public Guid KlasseId { get; set; }
    public string? KlasseTitle { get; set; }
    public bool IsCurrentKlasse { get; set; }
    public int Order { get; set; }
    public bool IsPublished { get; set; }
    public int PageCount { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public Guid? AuthorId { get; set; }
    public bool IsPdfExportEnabled { get; set; }
    public bool IsSharedWithSchool { get; set; }
    public bool IsExerciseLesson { get; set; }
    public bool HasVideo { get; set; }
    public string? VideoDisplayName { get; set; }
}
