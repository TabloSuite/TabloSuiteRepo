using HalloApp.Core.DataModels.BaseModel;
using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
namespace HalloApp.Core.DataModels.Exercises;

public class ExerciseOption : AuditableModel
{
    public Guid ExerciseId { get; set; }
    public Exercise Exercise { get; set; } = null!;

    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int Order { get; set; }
}







