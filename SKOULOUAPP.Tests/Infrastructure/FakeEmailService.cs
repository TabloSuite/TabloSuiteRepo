using SkoulouApp.Core.Services.Email;

namespace SkoulouApp.Tests.Infrastructure;

/// <summary>
/// E-Mail-Service für Tests. Es wird <b>kein</b> realer Versand durchgeführt:
/// Alle Nachrichten werden lediglich im Speicher gesammelt und können
/// anschließend geprüft werden.
/// </summary>
public sealed class FakeEmailService : IEmailService
{
    /// <summary>Alle "versendeten" Einzelnachrichten in Reihenfolge des Aufrufs.</summary>
    public List<(EmailRecipient Recipient, EmailMessage Message)> Gesendet { get; } = [];

    /// <summary>Alle "versendeten" Sammelnachrichten.</summary>
    public List<(IReadOnlyCollection<EmailRecipient> Recipients, EmailMessage Message)> GesendeteBatches { get; } = [];

    /// <summary>Wenn gesetzt, schlägt der Versand fehl (simuliert z. B. eine nicht existierende Adresse).</summary>
    public string? FehlerMeldung { get; set; }

    /// <summary>Wenn gesetzt, wirft der Versand eine Ausnahme (simuliert einen Netzwerkfehler).</summary>
    public Exception? WirftAusnahme { get; set; }

    /// <summary>Die zuletzt erzeugte Nachricht, oder <c>null</c> wenn noch nichts gesendet wurde.</summary>
    public EmailMessage? LetzteNachricht => Gesendet.Count == 0 ? null : Gesendet[^1].Message;

    public Task<EmailSendResult> SendAsync(EmailRecipient recipient, EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (WirftAusnahme is not null)
        {
            throw WirftAusnahme;
        }

        if (FehlerMeldung is not null)
        {
            return Task.FromResult(new EmailSendResult(false, FehlerMeldung));
        }

        Gesendet.Add((recipient, message));
        return Task.FromResult(new EmailSendResult(true));
    }

    public Task<EmailBatchResult> SendBatchAsync(IReadOnlyCollection<EmailRecipient> recipients, EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (WirftAusnahme is not null)
        {
            throw WirftAusnahme;
        }

        if (FehlerMeldung is not null)
        {
            return Task.FromResult(new EmailBatchResult(0, recipients.Count, [FehlerMeldung]));
        }

        GesendeteBatches.Add((recipients, message));
        return Task.FromResult(new EmailBatchResult(recipients.Count, 0, []));
    }
}
