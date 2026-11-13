namespace SkoulouApp.Core.Models.TeacherLessons;

/// <summary>Schreibgeschützte Übersicht eines von Lehrkräften mit der Schule geteilten Inhalts.</summary>
public class SharedTeacherContentItem
{
    public Guid ContentId { get; set; }
    public Guid? SchoolId { get; set; }
    public string SchoolName { get; set; } = string.Empty;
    public Guid? KlasseId { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CourseCode { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>Von der Direktion im Namen des Lehrers erfasst (« Saisi par la direction »).</summary>
    public bool IstVonDirektionErfasst { get; set; }
}
