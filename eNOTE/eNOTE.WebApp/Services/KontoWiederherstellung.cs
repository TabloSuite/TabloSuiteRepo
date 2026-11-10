using ENote.Core.DataModels.Identity;
using ENote.WebApp.Services.Mails;
using Microsoft.AspNetCore.Identity;

namespace ENote.WebApp.Services;

public class KontoWiederherstellung(UserManager<AppUser> users, IConfiguration configuration, ILogger<KontoWiederherstellung> logger, EmailService emailService, MailBenachrichtigungen? mails = null)
{
    public async Task AnfordernAsync(string pseudonym)
    {
        var user = await users.FindByNameAsync(pseudonym.Trim());
        if (user is null || user.IsBlocked || user.IstInternerKassierer || string.IsNullOrWhiteSpace(user.Email)
            || await users.IsInRoleAsync(user, AppRollen.Kassierer)) return;
        if (!emailService.IstKonfiguriert
            || !Uri.TryCreate(configuration["App:PublicBaseUrl"], UriKind.Absolute, out var basis)
            || basis.Scheme != "https")
        {
            logger.LogWarning("Passwortwiederherstellung: SMTP oder feste HTTPS-App-Adresse fehlt.");
            throw new InvalidOperationException("La récupération du mot de passe n'est pas encore disponible. Contactez l'administrateur.");
        }
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var link = new Uri(basis, $"/auth/reinitialiser?compte={user.Id}&code={Uri.EscapeDataString(token)}").AbsoluteUri;
        await emailService.SendeAsync(user.Email, "Réinitialisation du mot de passe", TabloSuiteMails.Reinitialisation(user.UserName ?? "", link), "reinitialisation");
    }

    public async Task<bool> ReinitialiseAsync(Guid id, string token, string passwort)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null || user.IsBlocked || user.IstInternerKassierer || await users.IsInRoleAsync(user, AppRollen.Kassierer)) return false;
        var result = await users.ResetPasswordAsync(user, token, passwort);
        if (!result.Succeeded) return false;
        user.ActiveSessionId = null;
        user.EmailConfirmed = true;
        var ok = (await users.UpdateAsync(user)).Succeeded;
        if (ok && mails is not null)
            await mails.PasswortGeaendertAsync(user.Id);
        return ok;
    }
}
