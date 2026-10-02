using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Identity;

namespace SkoulouApp.Core.DataModels.Schools;

public class LehrerFachZuordnung : AuditableModel
{
    public Guid LehrerId { get; set; }
    public AppUser Lehrer { get; set; } = null!;

    public Guid FachId { get; set; }
    public Fach Fach { get; set; } = null!;
}
