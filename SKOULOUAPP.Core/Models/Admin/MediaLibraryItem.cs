namespace SkoulouApp.Core.Models.Admin;

public class MediaLibraryItem
{
    public Guid MediaId { get; set; }
    public Guid? SchoolId { get; set; }
    public string SchoolName { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Length { get; set; }
    public string AltText { get; set; } = string.Empty;
    public string LessonTitle { get; set; } = string.Empty;
    public string CourseCode { get; set; } = string.Empty;
    public string KlasseCode { get; set; } = string.Empty;
    public string StorageHint { get; set; } = string.Empty;
}
