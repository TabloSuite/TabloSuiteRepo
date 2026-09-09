namespace HalloApp.Core.Models.TeacherLessons;

/// <summary>Schreibgeschützte Übersicht eines von Lehrkräften mit der Schule geteilten Inhalts.</summary>
public class SharedTeacherContentItem
{
    public Guid? SchoolId { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CourseCode { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
}
