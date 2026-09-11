using HalloApp.Core.DataModels.BaseModel;

namespace HalloApp.Core.DataModels.Schools;

public class Fach : AuditableModel
{
    public Guid SchoolId { get; set; }
    public School School { get; set; } = null!;
    public string Name { get; set; } = string.Empty;

    public ICollection<LehrerFachZuordnung> LehrerZuordnungen { get; set; } = new List<LehrerFachZuordnung>();
    public ICollection<HalloApp.Core.DataModels.Lessons.Lesson> Lessons { get; set; } = new List<HalloApp.Core.DataModels.Lessons.Lesson>();
    public ICollection<HalloApp.Core.DataModels.Lessons.TeacherLesson> TeacherLessons { get; set; } = new List<HalloApp.Core.DataModels.Lessons.TeacherLesson>();
}
