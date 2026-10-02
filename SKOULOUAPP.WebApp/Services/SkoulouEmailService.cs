using SkoulouApp.Core.Services.Email;

namespace SkoulouApp.WebApp.Services;

public sealed class SkoulouEmailService(IEmailService transport) : IEmailService
{
    public Task<EmailSendResult> SendAsync(EmailRecipient recipient, EmailMessage message, CancellationToken cancellationToken = default)
        => transport.SendAsync(recipient, EmailTemplates.EnsureLayout(message), cancellationToken);

    public Task<EmailBatchResult> SendBatchAsync(IReadOnlyCollection<EmailRecipient> recipients, EmailMessage message, CancellationToken cancellationToken = default)
        => transport.SendBatchAsync(recipients, EmailTemplates.EnsureLayout(message), cancellationToken);
}
