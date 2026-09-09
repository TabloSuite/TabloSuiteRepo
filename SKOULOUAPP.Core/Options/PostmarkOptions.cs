namespace HalloApp.Core.Options;

/// <summary>
/// Konfiguration für den E-Mail-Versand über Postmark.
/// Der ServerToken (API-Key) gehört in User Secrets bzw. Umgebungsvariablen, nicht in appsettings.json.
/// </summary>
public class PostmarkOptions
{
    public const string SectionName = "Postmark";

    /// <summary>Postmark Server API Token (Secret).</summary>
    public string ServerToken { get; set; } = string.Empty;

    /// <summary>Verifizierte Absenderadresse (Signatur/Domain bei Postmark).</summary>
    public string FromEmail { get; set; } = string.Empty;

    /// <summary>Angezeigter Absendername.</summary>
    public string FromName { get; set; } = "HalloApp";

    /// <summary>Zieladresse für das Kontaktformular / Support-Nachrichten.</summary>
    public string SupportEmail { get; set; } = string.Empty;

    /// <summary>Postmark Message Stream (Standard: "outbound").</summary>
    public string MessageStream { get; set; } = "outbound";

    /// <summary>Wenn false, wird kein realer Versand durchgeführt (z. B. lokal ohne Token).</summary>
    public bool Enabled { get; set; } = true;
}
