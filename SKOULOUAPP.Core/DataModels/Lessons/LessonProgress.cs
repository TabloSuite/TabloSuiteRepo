using HalloApp.Core.DataModels.BaseModel;
using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
namespace HalloApp.Core.DataModels.Lessons;

public class LessonProgress : AuditableModel
{
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;

    public Guid StudentId { get; set; }
    public AppUser Student { get; set; } = null!;

    public int CorrectExerciseCount { get; set; }
    public int TotalExerciseCount { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
}







