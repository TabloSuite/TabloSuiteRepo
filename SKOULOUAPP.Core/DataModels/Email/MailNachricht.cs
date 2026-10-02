using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels.Email;

public class MailNachricht : AuditableModel
{
    public Guid? SchulID { get; set; }
    public Guid? GesendetVonID { get; set; }
    public string Absender { get; set; } = string.Empty;
    public string Empfaenger { get; set; } = string.Empty;
    public string Betreff { get; set; } = string.Empty;
    public string Inhalt { get; set; } = string.Empty;
    public string? PostmarkMessageID { get; set; }
    public bool IstEingehend { get; set; }
}
