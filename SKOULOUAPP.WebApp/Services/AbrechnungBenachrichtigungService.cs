using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services.Email;
using SkoulouApp.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.WebApp.Services;

public sealed class AbrechnungBenachrichtigungService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IEmailService emailService,
    ILogger<AbrechnungBenachrichtigungService> logger)
{
    public async Task<(bool Erlaubt, int Gesendet, int Fehlgeschlagen)> ManuelleRechnungErneutSendenAsync(
        Guid periodeId, Guid userId, CancellationToken cancellationToken = default)
    {
        await using (var db = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var erlaubt = await db.Abrechnungsperioden.AsNoTracking()
                .AnyAsync(item => item.Id == periodeId && item.Status == AbrechnungStatus.Erstellt
                    && !item.AutomatischDurchKlassenabschlussErstellt && !item.IstSammelabrechnungAusGeschlossenenKlassen
                    && db.UserRoles.Any(userRole => userRole.UserId == userId
                        && (userRole.Role.Name == Rollen.SuperAdmin
                            || (userRole.Role.Name == Rollen.ProduktOwner
                                && db.UserSchools.Any(link => link.AppUserId == userId && link.SchoolId == item.SchulID)))), cancellationToken);
            if (!erlaubt)
                return (false, 0, 0);
        }

        var (gesendet, fehlgeschlagen) = await ManuelleRechnungSendenAsync(periodeId, cancellationToken);
        return (true, gesendet, fehlgeschlagen);
    }

    public async Task<(int Gesendet, int Fehlgeschlagen)> ManuelleRechnungSendenAsync(
        Guid periodeId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var periode = await db.Abrechnungsperioden.AsNoTracking()
            .Where(item => item.Id == periodeId && item.Status == AbrechnungStatus.Erstellt
                && !item.AutomatischDurchKlassenabschlussErstellt && !item.IstSammelabrechnungAusGeschlossenenKlassen)
            .Select(item => new { item.SchulID, item.Schule.Name, item.VonUtc, item.BisUtc, item.Umsatz, item.AnzahlAbonnements })
            .FirstOrDefaultAsync(cancellationToken);
        if (periode is null)
            return (0, 0);

        var recipients = await db.Users.AsNoTracking()
            .Where(user => !user.IsBlocked && user.Email != null && user.Email != ""
                && user.UserSchools.Any(link => link.SchoolId == periode.SchulID && link.CanManageStudents)
                && user.UserRoles.Any(role => role.Role.Name == Rollen.SchulLeiter))
            .Select(user => new { user.Email, user.Vorname })
            .ToListAsync(cancellationToken);
        var validRecipients = recipients
            .Where(user => new EmailAddressAttribute().IsValid(user.Email))
            .GroupBy(user => user.Email!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (validRecipients.Count == 0)
        {
            logger.LogWarning("Keine aktive Direktion mit gültiger E-Mail für Abrechnung {PeriodeId} in Schule {SchulID} gefunden.", periodeId, periode.SchulID);
            return (0, 1);
        }

        var amount = periode.Umsatz.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"));
        var school = WebUtility.HtmlEncode(periode.Name);
        var html = EmailTemplates.Wrap("Nouvelle facturation SKOULOU",
            $"<p>Bonjour,</p><p>Une facturation SKOULOU a été créée pour votre établissement <strong>{school}</strong>.</p>" +
            $"<p>Période : du {periode.VonUtc:dd/MM/yyyy} au {periode.BisUtc:dd/MM/yyyy}<br/>" +
            $"Abonnements : {periode.AnzahlAbonnements}<br/>Montant à virer : <strong>{amount} FCFA</strong>.</p>" +
            "<p>Consultez la facturation dans votre espace SKOULOU. Après avoir effectué le virement, signalez-le dans l'application. " +
            "L'Administrateur vérifiera ensuite la réception sur le compte avant de confirmer la facturation.</p>" +
            "<p>À bientôt,<br/>L'équipe SKOULOU</p>");
        var message = new EmailMessage
        {
            Subject = "Facturation SKOULOU à régler",
            HtmlBody = html,
            TextBody = EmailTemplates.ToPlainText(html)
        };

        var sent = 0;
        var failed = 0;
        foreach (var recipient in validRecipients)
        {
            try
            {
                var result = await emailService.SendAsync(new EmailRecipient(recipient.Email!, recipient.Vorname), message, cancellationToken);
                if (result.Succeeded)
                    sent++;
                else
                {
                    failed++;
                    logger.LogWarning("E-Mail zur Abrechnung {PeriodeId} an {Email} fehlgeschlagen: {Error}", periodeId, recipient.Email, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(ex, "E-Mail zur Abrechnung {PeriodeId} an {Email} fehlgeschlagen.", periodeId, recipient.Email);
            }
        }

        return (sent, failed);
    }
}
