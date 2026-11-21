namespace ENote.Core.DataModels.Infrastruktur;

/// <summary>
/// Eingehende E-Mail an contact@tablosuite.com, die eNOTE betrifft (Betreff mit « [eNOTE] » oder « eNOTE »).
/// Postmark liefert sie per Webhook; der Inhalt wird nur als Text gespeichert und angezeigt (kein fremdes HTML).
/// </summary>
public class EingangsMail
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Absender { get; set; } = string.Empty;
    public string Empfaenger { get; set; } = string.Empty;
    public string Betreff { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    /// <summary>Postmark MessageID – verhindert doppelte Einträge bei wiederholter Zustellung.</summary>
    public string? AnbieterId { get; set; }

    public DateTime EmpfangenUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;
    public bool IstGelesen { get; set; }
    public DateTime? BeantwortetUtc { get; set; }
}
