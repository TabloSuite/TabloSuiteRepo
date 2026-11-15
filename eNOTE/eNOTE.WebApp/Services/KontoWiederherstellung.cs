using ENote.Core.DataModels.Identity;
using ENote.WebApp.Services.Mails;
using Microsoft.AspNetCore.Identity;

namespace ENote.WebApp.Services;

public class KontoWiederherstellung(UserManager<AppUser> users, IConfiguration configuration, ILogger<KontoWiederherstellung> logger, EmailService emailService, MailBenachrichtigungen? mails = null, Sicherheit.MissbrauchsBremse? bremse = null)
{
    /// <summary>
    /// Link anfordern. Antwort der Seite ist immer neutral; zu viele Anfragen von einem Anschluss (10 in 10 Minuten) oder
    /// für dasselbe Konto (3 je Stunde – sonst ließe sich ein Postfach mit Mails fluten) werden still verworfen.
    /// </summary>
    public async Task AnfordernAsync(string pseudonym, string? anschluss = null)
    {
        if (bremse is not null)
        {
            if (!bremse.IstErlaubt("oublie-anschluss", anschluss, Sicherheit.MissbrauchsBremse.OublieJeAnschluss)) return;
            bremse.Merken("oublie-anschluss", anschluss);
        }
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
        if (bremse is not null && !bremse.IstErlaubt("oublie-konto", user.Id.ToString(), Sicherheit.MissbrauchsBremse.OublieJeKonto)) return;
        bremse?.Merken("oublie-konto", user.Id.ToString());
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
        // Nach Fehlversuchen gesperrt und dann per Mail-Link zurückgesetzt: die Sperre fällt, sonst scheitert die Anmeldung gleich danach.
        (user.AccessFailedCount, user.LockoutEnd) = (0, null);
        var ok = (await users.UpdateAsync(user)).Succeeded;
        if (ok && mails is not null)
            await mails.PasswortGeaendertAsync(user.Id);
        return ok;
    }
}
