namespace SkoulouApp.Core.DataModels.Schools;

public class Ankuendigung
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Titel { get; set; } = string.Empty;
    public string InhaltHtml { get; set; } = string.Empty;
    public DateTime ErstelltAmUtc { get; set; } = DateTime.UtcNow;
    public DateTime AblaufAmUtc { get; set; }
}
