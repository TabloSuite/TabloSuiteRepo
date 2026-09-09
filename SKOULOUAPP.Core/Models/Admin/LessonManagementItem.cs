namespace HalloApp.Core.Models.Admin;

public class LessonManagementItem
{
    public Guid LessonId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int Order { get; set; }
    public int ExerciseCount { get; set; }
}
