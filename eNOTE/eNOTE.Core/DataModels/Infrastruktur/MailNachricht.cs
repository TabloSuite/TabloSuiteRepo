namespace ENote.Core.DataModels.Infrastruktur;

/// <summary>Ausgehende E-Mail (Willkommensmail, Benachrichtigungen) – Versand-Queue.</summary>
public class MailNachricht
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Empfaenger { get; set; } = string.Empty;
    public string Betreff { get; set; } = string.Empty;
    public string HtmlInhalt { get; set; } = string.Empty;
    public bool IstGesendet { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? GesendetUtc { get; set; }
}
