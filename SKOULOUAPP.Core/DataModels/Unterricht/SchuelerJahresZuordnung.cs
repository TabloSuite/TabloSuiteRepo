using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;

namespace SkoulouApp.Core.DataModels.Unterricht;

/// <summary>Historischer Klassenbezug; eine Versetzung ändert frühere Schuljahre nicht.</summary>
public class SchuelerJahresZuordnung : AuditableModel
{
    public Guid SchulID { get; set; }
    public School Schule { get; set; } = null!;
    public Guid SchuljahrID { get; set; }
    public Schuljahr Schuljahr { get; set; } = null!;
    public Guid KlasseID { get; set; }
    public Klasse Klasse { get; set; } = null!;
    public Guid SchuelerID { get; set; }
    public AppUser Schueler { get; set; } = null!;
    public bool IstWiederholer { get; set; }
}
