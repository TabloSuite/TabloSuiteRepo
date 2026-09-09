namespace HalloApp.Core.Services.Email;

/// <summary>
/// Ein einzelner E-Mail-Empfänger.
/// </summary>
public sealed record EmailRecipient(string Email, string? Name = null);

/// <summary>
/// Eine zu versendende E-Mail-Nachricht.
/// </summary>
public sealed class EmailMessage
{
    public required string Subject { get; init; }

    /// <summary>HTML-Inhalt der Nachricht.</summary>
    public required string HtmlBody { get; init; }

    /// <summary>Optionaler Klartext-Inhalt (Fallback).</summary>
    public string? TextBody { get; init; }

    /// <summary>Optionale Antwortadresse (z. B. für Kontaktformular-Absender).</summary>
    public string? ReplyTo { get; init; }
}
