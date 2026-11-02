using System.ComponentModel.DataAnnotations;
using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Courses;

namespace SkoulouApp.Core.DataModels.Unterricht;

public class Stundenplan : AuditableModel
{
    public Guid SchulID { get; set; }
    public Guid SchuljahrID { get; set; }
    public Schuljahr Schuljahr { get; set; } = null!;
    public Guid KlasseID { get; set; }
    public Klasse Klasse { get; set; } = null!;
    [MaxLength(120)] public string Bezeichnung { get; set; } = string.Empty;
    public DateOnly GueltigAb { get; set; }
    public DateOnly GueltigBis { get; set; }
    public bool IstVeroeffentlicht { get; set; }
    public byte[] ZeilenVersion { get; set; } = [];
}
