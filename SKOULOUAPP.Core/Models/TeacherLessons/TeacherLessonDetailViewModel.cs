namespace HalloApp.Core.Models.TeacherLessons;

/// <summary>
/// Detailansicht eines Kursskripts inkl. aller Seiten – für den seitenweisen
/// Schüler-Reader und die Lehrer-Vorschau.
/// </summary>
public class TeacherLessonDetailViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public Guid? FachId { get; set; }
    public string? FachName { get; set; }
    public string? CourseTheme { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public bool IsPdfExportEnabled { get; set; }
    public Guid? VideoAssetId { get; set; }
    public string? VideoDisplayName { get; set; }
    public TimeSpan? VideoDuration { get; set; }
    public string? VideoUrl { get; set; }
    public bool HasVideo => VideoAssetId.HasValue;
    public List<TeacherLessonPageViewModel> Pages { get; set; } = [];
}

public class TeacherLessonPageViewModel
{
    public Guid Id { get; set; }
    public int PageNumber { get; set; }
    public string ContentHtml { get; set; } = string.Empty;
}
