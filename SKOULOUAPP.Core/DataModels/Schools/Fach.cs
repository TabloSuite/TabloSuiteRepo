using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels.Schools;

public class Fach : AuditableModel
{
    public Guid SchoolId { get; set; }
    public School School { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    /// <summary>Fächergruppe für den Bulletin-Bilan, z. B. « Lettres », « Sciences », « Autres » (leer = ohne Gruppe).</summary>
    public string? Gruppe { get; set; }
    /// <summary>Reihenfolge im Bulletin (klein zuerst, danach alphabetisch).</summary>
    public int Reihenfolge { get; set; }

    public ICollection<LehrerFachZuordnung> LehrerZuordnungen { get; set; } = new List<LehrerFachZuordnung>();
    public ICollection<SkoulouApp.Core.DataModels.Lessons.Lesson> Lessons { get; set; } = new List<SkoulouApp.Core.DataModels.Lessons.Lesson>();
    public ICollection<SkoulouApp.Core.DataModels.Lessons.TeacherLesson> TeacherLessons { get; set; } = new List<SkoulouApp.Core.DataModels.Lessons.TeacherLesson>();
}
