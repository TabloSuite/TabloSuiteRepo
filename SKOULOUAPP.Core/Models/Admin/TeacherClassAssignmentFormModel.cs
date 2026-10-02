namespace SkoulouApp.Core.Models.Admin;

public class TeacherClassAssignmentFormModel
{
    public Guid SchoolId { get; set; }
    public Guid KlasseId { get; set; }
    public decimal Wochenstunden { get; set; } = 1m;
}
