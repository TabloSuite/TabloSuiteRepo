using HalloApp.Core.DataModels.BaseModel;
using HalloApp.Core.DataModels.Identity;

namespace HalloApp.Core.DataModels.Lessons;

public class FormelFavorit : AuditableModel
{
    public Guid BenutzerID { get; set; }
    public AppUser Benutzer { get; set; } = null!;

    public string LaTeX { get; set; } = string.Empty;
    public string Bezeichnung { get; set; } = string.Empty;
    public string Vorschau { get; set; } = string.Empty;
}
