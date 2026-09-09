namespace HalloApp.Core.Models.Admin;

public class CourseManagementViewModel
{
    public Guid CourseId { get; set; }
    public Guid? SchoolId { get; set; }
    public string? SchoolName { get; set; }
    public bool IstSystemNiveau { get; set; }
    public bool IsSchoolSpecific => SchoolId.HasValue;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CEFRGroup { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int RecommendedLessonCount { get; set; }
    public decimal Price { get; set; }
    public int ExamCount { get; set; }
    public int MediaCount { get; set; }
    public int StudentCount { get; set; }
    public List<LessonManagementItem> Lessons { get; set; } = [];
}
