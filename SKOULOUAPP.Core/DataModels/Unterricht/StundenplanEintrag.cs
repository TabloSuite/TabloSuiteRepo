using System.ComponentModel.DataAnnotations;
using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels.Unterricht;

public class StundenplanEintrag : AuditableModel
{
    public Guid StundenplanID { get; set; }
    public Stundenplan Stundenplan { get; set; } = null!;
    public Guid UnterrichtsZuordnungID { get; set; }
    public UnterrichtsZuordnung UnterrichtsZuordnung { get; set; } = null!;
    public DayOfWeek Wochentag { get; set; }
    public TimeOnly Beginn { get; set; }
    public TimeOnly Ende { get; set; }
    [MaxLength(80)] public string? Raum { get; set; }
}
