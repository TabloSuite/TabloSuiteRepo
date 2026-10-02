namespace SkoulouApp.Core.Services.Email;

/// <summary>
/// Austauschbare Abstraktion für den E-Mail-Versand.
/// Standardimplementierung nutzt Postmark (<see cref="PostmarkEmailService"/>).
/// </summary>
public interface IEmailService
{
    /// <summary>Sendet eine E-Mail an einen einzelnen Empfänger.</summary>
    Task<EmailSendResult> SendAsync(EmailRecipient recipient, EmailMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sendet dieselbe Nachricht an mehrere Empfänger (individuell, je eine Mail pro Empfänger).
    /// Große Empfängermengen werden intern in Batches aufgeteilt.
    /// </summary>
    Task<EmailBatchResult> SendBatchAsync(IReadOnlyCollection<EmailRecipient> recipients, EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Ergebnis eines Einzelversands.</summary>
public sealed record EmailSendResult(bool Succeeded, string? ErrorMessage = null);

/// <summary>Ergebnis eines Massenversands.</summary>
public sealed record EmailBatchResult(int SentCount, int FailedCount, IReadOnlyList<string> Errors)
{
    public bool Succeeded => FailedCount == 0;
}
