using HalloApp.Core.DataModels.BaseModel;
using HalloApp.Core.DataModels.Identity;

namespace HalloApp.Core.DataModels.Schools;

public class LehrerFachZuordnung : AuditableModel
{
    public Guid LehrerId { get; set; }
    public AppUser Lehrer { get; set; } = null!;

    public Guid FachId { get; set; }
    public Fach Fach { get; set; } = null!;
}
