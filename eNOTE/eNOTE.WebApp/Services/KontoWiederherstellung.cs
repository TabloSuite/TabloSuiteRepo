using System.Net;
using System.Net.Mail;
using ENote.Core.DataModels.Identity;
using Microsoft.AspNetCore.Identity;

namespace ENote.WebApp.Services;

public class KontoWiederherstellung(UserManager<AppUser> users, IConfiguration configuration, ILogger<KontoWiederherstellung> logger)
{
    public async Task AnfordernAsync(string pseudonym)
    {
        var user = await users.FindByNameAsync(pseudonym.Trim());
        if (user is null || user.IsBlocked || user.IstInternerKassierer || string.IsNullOrWhiteSpace(user.Email)
            || await users.IsInRoleAsync(user, AppRollen.Kassierer)) return;
        var host = configuration["Mail:SmtpHost"];
        var absender = configuration["Mail:Absender"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(absender)
            || !Uri.TryCreate(configuration["App:PublicBaseUrl"], UriKind.Absolute, out var basis)
            || basis.Scheme != "https")
        {
            logger.LogWarning("Passwortwiederherstellung: SMTP oder feste HTTPS-App-Adresse fehlt.");
            throw new InvalidOperationException("La récupération du mot de passe n'est pas encore disponible. Contactez l'administrateur.");
        }
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var link = new Uri(basis, $"/auth/reinitialiser?compte={user.Id}&code={Uri.EscapeDataString(token)}").AbsoluteUri;
        using var message = new MailMessage(absender, user.Email)
        {
            Subject = "eNOTE · Réinitialisation du mot de passe",
            Body = $"Compte : {user.UserName}\n\nPour choisir un nouveau mot de passe :\n{link}\n\nSi vous n'êtes pas à l'origine de cette demande, ignorez ce message."
        };
        using var smtp = new SmtpClient(host, configuration.GetValue("Mail:SmtpPort", 587))
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(configuration["Mail:SmtpUtilisateur"], configuration["Mail:SmtpMotDePasse"])
        };
        await smtp.SendMailAsync(message);
    }

    public async Task<bool> ReinitialiseAsync(Guid id, string token, string passwort)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null || user.IsBlocked || user.IstInternerKassierer || await users.IsInRoleAsync(user, AppRollen.Kassierer)) return false;
        var result = await users.ResetPasswordAsync(user, token, passwort);
        if (!result.Succeeded) return false;
        user.ActiveSessionId = null;
        user.EmailConfirmed = true;
        return (await users.UpdateAsync(user)).Succeeded;
    }
}
