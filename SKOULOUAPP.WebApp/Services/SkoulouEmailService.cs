using SkoulouApp.Core.Services.Email;

namespace SkoulouApp.WebApp.Services;

public sealed class SkoulouEmailService(IEmailService transport) : IEmailService
{
    /// <summary>App-Kennung im Betreff: alle Apps teilen contact@tablosuite.com, Antworten werden daran zugeordnet.</summary>
    public const string AppKennung = "[SKOULOU]";

    public Task<EmailSendResult> SendAsync(EmailRecipient recipient, EmailMessage message, CancellationToken cancellationToken = default)
        => transport.SendAsync(recipient, MitAppName(EmailTemplates.EnsureLayout(message)), cancellationToken);

    public Task<EmailBatchResult> SendBatchAsync(IReadOnlyCollection<EmailRecipient> recipients, EmailMessage message, CancellationToken cancellationToken = default)
        => transport.SendBatchAsync(recipients, MitAppName(EmailTemplates.EnsureLayout(message)), cancellationToken);

    /// <summary>Setzt « [SKOULOU] » vor den Betreff (nicht doppelt, auch nicht bei « Re: [SKOULOU] … »).</summary>
    public static string MitAppName(string betreff)
        => betreff.Contains(AppKennung, StringComparison.OrdinalIgnoreCase) ? betreff : $"{AppKennung} {betreff}".TrimEnd();

    private static EmailMessage MitAppName(EmailMessage message) => new()
    {
        Subject = MitAppName(message.Subject),
        HtmlBody = message.HtmlBody,
        TextBody = message.TextBody,
        ReplyTo = message.ReplyTo
    };
}
