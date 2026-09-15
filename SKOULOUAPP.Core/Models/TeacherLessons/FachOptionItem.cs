namespace HalloApp.Core.Models.TeacherLessons;

public class FachOptionItem
{
    public Guid FachId { get; set; }
    public Guid SchoolId { get; set; }
    public string SchoolName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IstLehrerZugewiesen { get; set; }
}
