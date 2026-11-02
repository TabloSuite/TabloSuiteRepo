using System.ComponentModel.DataAnnotations;
using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Schools;

namespace SkoulouApp.Core.DataModels.Unterricht;

public class Schuljahr : AuditableModel
{
    public Guid SchulID { get; set; }
    public School Schule { get; set; } = null!;
    [MaxLength(80)]
    public string Bezeichnung { get; set; } = string.Empty;
    public DateOnly Beginn { get; set; }
    public DateOnly Ende { get; set; }
    public bool IstGeschlossen { get; set; }
}
