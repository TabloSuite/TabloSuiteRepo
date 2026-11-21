namespace ENote.Core.DataModels.Infrastruktur;

/// <summary>Ausgehende E-Mail (Willkommensmail, Benachrichtigungen) – Versand-Queue.</summary>
public class MailNachricht
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Empfaenger { get; set; } = string.Empty;
    public string Betreff { get; set; } = string.Empty;
    public string HtmlInhalt { get; set; } = string.Empty;
    public bool IstGesendet { get; set; }
    public DateTime CreatedAtUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;
    public DateTime? GesendetUtc { get; set; }

    /// <summary>Art der Nachricht (z. B. « facture », « bienvenue ») – Postmark-Tag und Filter in der Übersicht.</summary>
    public string? Kategorie { get; set; }

    /// <summary>Anzahl Versandversuche (Wiederholung durch den Hintergrunddienst, max. 5).</summary>
    public int Versuche { get; set; }

    /// <summary>Letzte Fehlermeldung des Versanddienstes (gekürzt).</summary>
    public string? LetzterFehler { get; set; }

    /// <summary>Kennung beim Anbieter (Postmark MessageID) zur Nachverfolgung.</summary>
    public string? AnbieterId { get; set; }
}
