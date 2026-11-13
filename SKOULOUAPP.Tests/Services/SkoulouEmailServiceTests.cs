using SkoulouApp.Core.Services.Email;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class SkoulouEmailServiceTests
{
    [Fact]
    public async Task Einzelmail_RohesHtml_ErhaeltLayoutUndMetadaten()
    {
        var transport = new FakeEmailService();
        var service = new SkoulouEmailService(transport);
        var original = new EmailMessage
        {
            Subject = "Bonjour & bienvenue",
            HtmlBody = "<p>Contenu du message</p>",
            TextBody = "Texte personnalisé",
            ReplyTo = "reply@example.org"
        };

        var result = await service.SendAsync(new EmailRecipient("user@example.org"), original);

        Assert.True(result.Succeeded);
        var sent = Assert.Single(transport.Gesendet).Message;
        Assert.Contains("SKOULOUAPP-EMAIL-LAYOUT", sent.HtmlBody);
        Assert.Contains("<p>Contenu du message</p>", sent.HtmlBody);
        Assert.Contains("Bonjour &amp; bienvenue", sent.HtmlBody);
        // Gemeinsame Adresse contact@tablosuite.com: der App-Name steht immer im Betreff.
        Assert.Equal("[SKOULOU] Bonjour & bienvenue", sent.Subject);
        Assert.Equal(original.TextBody, sent.TextBody);
        Assert.Equal(original.ReplyTo, sent.ReplyTo);
        Assert.Equal("<p>Contenu du message</p>", original.HtmlBody);
    }

    [Fact]
    public async Task Einzelmail_BestehendesLayout_WirdNichtErneutEingefasst()
    {
        var transport = new FakeEmailService();
        var service = new SkoulouEmailService(transport);
        var html = EmailTemplates.Wrap("Bienvenue", "<p>Déjà formaté</p>");
        var original = new EmailMessage { Subject = "Bienvenue", HtmlBody = html };

        await service.SendAsync(new EmailRecipient("user@example.org"), original);

        var gesendet = Assert.Single(transport.Gesendet).Message;
        Assert.Same(original.HtmlBody, gesendet.HtmlBody);          // Layout nicht erneut eingefasst
        Assert.Equal("[SKOULOU] Bienvenue", gesendet.Subject);       // nur der App-Name kommt dazu
        Assert.Equal(1, html.Split("SKOULOUAPP-EMAIL-LAYOUT").Length - 1);
    }

    [Fact]
    public async Task Gruppenmail_RohesHtml_BekommtEinmalLayoutUndTextFallback()
    {
        var transport = new FakeEmailService();
        var service = new SkoulouEmailService(transport);
        var recipients = new[] { new EmailRecipient("a@example.org"), new EmailRecipient("b@example.org") };

        var result = await service.SendBatchAsync(recipients, new EmailMessage { Subject = "Nouvelles", HtmlBody = "<p>Bonjour à tous</p>" });

        Assert.Equal(2, result.SentCount);
        var sent = Assert.Single(transport.GesendeteBatches);
        Assert.Equal(2, sent.Recipients.Count);
        Assert.Contains("SKOULOUAPP-EMAIL-LAYOUT", sent.Message.HtmlBody);
        Assert.Equal("Bonjour à tous", sent.Message.TextBody);
    }
}
