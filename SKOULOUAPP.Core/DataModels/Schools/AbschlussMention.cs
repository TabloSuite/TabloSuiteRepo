using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels.Schools;

public class AbschlussMention : AuditableModel
{
    public Guid SchulID { get; set; }
    public School Schule { get; set; } = null!;
    public string Text { get; set; } = string.Empty;
}
