using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
namespace SkoulouApp.Core.DataModels.Lessons;

public class LessonMedia : AuditableModel
{
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;

    public string MediaType { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string AltText { get; set; } = string.Empty;
    public string StorageProvider { get; set; } = "LocalFileSystem";
}







