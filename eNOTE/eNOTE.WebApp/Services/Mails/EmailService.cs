using System.Net;
using System.Net.Mail;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services.Mails;

/// <summary>
/// Zentraler E-Mail-Versand für die gesamte TabloSuite-Plattform.
/// Branding und Absender sind bewusst app-übergreifend (TabloSuite), nicht eNOTE-spezifisch.
/// Ohne SMTP-Konfiguration (Mail:SmtpHost) werden Nachrichten nur in die Queue geschrieben.
/// </summary>
public class EmailService(IConfiguration configuration, IDbContextFactory<ApplicationDbContext> dbContextFactory, ILogger<EmailService> logger)
{
    private string? SmtpHost => configuration["Mail:SmtpHost"];
    private string Absender => configuration["Mail:Absender"] ?? "contact@tablosuite.com";
    private string AbsenderName => configuration["Mail:AbsenderName"] ?? "TabloSuite";

    public bool IstKonfiguriert => !string.IsNullOrWhiteSpace(SmtpHost);

    /// <summary>Stellt eine E-Mail in die Queue und versucht den sofortigen Versand.</summary>
    public async Task SendeAsync(string empfaenger, string betreff, string htmlInhalt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(empfaenger))
        {
            logger.LogWarning("E-Mail ohne Empfänger verworfen: {Betreff}", betreff);
            return;
        }

        Guid nachrichtId = Guid.NewGuid();
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var nachricht = new ENote.Core.DataModels.Infrastruktur.MailNachricht
            {
                Id = nachrichtId,
                Empfaenger = empfaenger.Trim(),
                Betreff = betreff,
                HtmlInhalt = htmlInhalt
            };
            db.Set<ENote.Core.DataModels.Infrastruktur.MailNachricht>().Add(nachricht);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mail-Queue nicht verfügbar; versuche Direktversand.");
        }

        if (!IstKonfiguriert)
        {
            logger.LogInformation("E-Mail-Versand nicht konfiguriert (Mail:SmtpHost fehlt). Nachricht {Id} bleibt in der Queue.", nachrichtId);
            return;
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(Absender, AbsenderName),
                Subject = betreff,
                Body = htmlInhalt,
                IsBodyHtml = true
            };
            message.To.Add(empfaenger.Trim());

            using var smtp = new SmtpClient(SmtpHost, configuration.GetValue("Mail:SmtpPort", 587))
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(configuration["Mail:SmtpUtilisateur"], configuration["Mail:SmtpMotDePasse"])
            };
            await smtp.SendMailAsync(message, cancellationToken);

            try
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                await db.Set<ENote.Core.DataModels.Infrastruktur.MailNachricht>()
                    .Where(m => m.Id == nachrichtId)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.IstGesendet, true).SetProperty(m => m.GesendetUtc, DateTime.UtcNow), cancellationToken);
            }
            catch { /* Queue-Update ist optional */ }

            logger.LogInformation("E-Mail gesendet an {Empfaenger}: {Betreff}", empfaenger, betreff);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "E-Mail-Versand an {Empfaenger} fehlgeschlagen: {Betreff}", empfaenger, betreff);
        }
    }
}
