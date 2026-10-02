using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels.Schools;

public class Fach : AuditableModel
{
    public Guid SchoolId { get; set; }
    public School School { get; set; } = null!;
    public string Name { get; set; } = string.Empty;

    public ICollection<LehrerFachZuordnung> LehrerZuordnungen { get; set; } = new List<LehrerFachZuordnung>();
    public ICollection<SkoulouApp.Core.DataModels.Lessons.Lesson> Lessons { get; set; } = new List<SkoulouApp.Core.DataModels.Lessons.Lesson>();
    public ICollection<SkoulouApp.Core.DataModels.Lessons.TeacherLesson> TeacherLessons { get; set; } = new List<SkoulouApp.Core.DataModels.Lessons.TeacherLesson>();
}
