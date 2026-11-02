using System.ComponentModel.DataAnnotations.Schema;
using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;

namespace SkoulouApp.Core.DataModels.Unterricht;

public class UnterrichtsZuordnung : AuditableModel
{
    public Guid SchulID { get; set; }
    public School Schule { get; set; } = null!;
    public Guid SchuljahrID { get; set; }
    public Schuljahr Schuljahr { get; set; } = null!;
    public Guid KlasseID { get; set; }
    public Klasse Klasse { get; set; } = null!;
    public Guid FachID { get; set; }
    public Fach Fach { get; set; } = null!;
    public Guid LehrerID { get; set; }
    public AppUser Lehrer { get; set; } = null!;
    /// <summary>Planung; verändert keine provisionsrelevanten Wochenstunden.</summary>
    [Column(TypeName = "decimal(7,2)")]
    public decimal Wochenstunden { get; set; }
}
