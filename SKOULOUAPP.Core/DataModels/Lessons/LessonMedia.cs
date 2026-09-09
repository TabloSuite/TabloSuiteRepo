using HalloApp.Core.DataModels.BaseModel;
using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
namespace HalloApp.Core.DataModels.Lessons;

public class LessonMedia : AuditableModel
{
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;

    public string MediaType { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string AltText { get; set; } = string.Empty;
    public string StorageProvider { get; set; } = "LocalFileSystem";
}







