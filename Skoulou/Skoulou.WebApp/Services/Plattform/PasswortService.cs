using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>
/// Passwort ändern, vergessen und zurücksetzen (HTTP-Endpunkte, da das Anmelde-Cookie erneuert wird).
/// Jede Änderung beendet andere Sitzungen; Bestätigungsmails enthalten nie Passwort oder Token.
/// </summary>
public sealed class PasswortService(
    UserManager<Konto> users,
    IDbContextFactory<SkoulouDbContext> dbFactory,
    MailAusgangService mails,
    AuditService audit,
    SitzungsPruefung sitzungen,
    IOptions<AppOptionen> app,
    ILogger<PasswortService> logger)
{
    public const string MeldungUngueltigerLink = "Le lien de réinitialisation est invalide ou a expiré.";
    public const string MeldungUnvollstaendig = "Le lien de réinitialisation est invalide ou incomplet.";
    public const string MeldungBestaetigung = "Les mots de passe ne correspondent pas.";
    public const string MeldungAktuellFalsch = "Le mot de passe actuel est incorrect.";

    /// <summary>Prüft neues Passwort und Bestätigung (ohne DB).</summary>
    public static string? PruefeNeues(string? neu, string? bestaetigung)
        => PasswortRegel.Pruefe(neu) ?? (neu != bestaetigung ? MeldungBestaetigung : null);

    /// <summary>Eigenes Passwort ändern. Fehlermeldung oder null bei Erfolg (Konto für das neue Cookie).</summary>
    public async Task<(string? Fehler, Konto? Konto)> AendernAsync(Guid kontoId, string? aktuell, string? neu, string? bestaetigung)
    {
        if (PruefeNeues(neu, bestaetigung) is { } fehler) return (fehler, null);
        var konto = await users.FindByIdAsync(kontoId.ToString());
        if (konto is null) return ("Votre compte est introuvable.", null);
        if (string.IsNullOrEmpty(aktuell) || !await users.CheckPasswordAsync(konto, aktuell)) return (MeldungAktuellFalsch, null);
        if (aktuell == neu) return ("Choisissez un mot de passe différent de l'actuel.", null);

        var ergebnis = await users.ChangePasswordAsync(konto, aktuell, neu!);
        if (!ergebnis.Succeeded) return (PasswortRegel.Pruefe(neu) ?? "Le mot de passe n'a pas pu être modifié.", null);

        await NachWechselAsync(konto, AuditAktionen.PasswortGeaendert, "Mot de passe modifié par l'utilisateur.");
        await BestaetigungAsync(konto, "Mot de passe modifié", "Le mot de passe de votre compte SKOULOU vient d'être modifié.");
        return (null, konto);
    }

    /// <summary>« Mot de passe oublié » mit E-Mail oder Identifiant. Antwort ist immer neutral.</summary>
    public async Task AnfordernAsync(string? eingabe, CancellationToken ct = default)
    {
        var wert = eingabe?.Trim();
        if (string.IsNullOrEmpty(wert) || wert.Length > 256) return;

        List<Konto> konten;
        if (wert.Contains('@'))
        {
            var normal = users.NormalizeEmail(wert);
            konten = await users.Users.Where(k => k.NormalizedEmail == normal && k.Status == KontoStatus.Actif)
                .OrderBy(k => k.UserName).Take(10).ToListAsync(ct);
        }
        else
        {
            var konto = await users.FindByNameAsync(wert);
            konten = konto is { Status: KontoStatus.Actif } && !string.IsNullOrWhiteSpace(konto.Email) ? [konto] : [];
        }
        if (konten.Count == 0) return;

        var adresse = konten[0].Email!;
        var bloecke = new List<string>();
        foreach (var konto in konten)
        {
            var token = await users.GeneratePasswordResetTokenAsync(konto);
            var link = app.Value.Url($"/auth/reinitialiser?id={konto.Id}&token={Uri.EscapeDataString(token)}");
            bloecke.Add(MailLayout.InfoKasten(
                $"<p style=\"margin:0 0 6px;\">Identifiant : <strong>{WebUtility.HtmlEncode(konto.UserName)}</strong></p>"
                + MailLayout.Knopf(link, "Choisir un nouveau mot de passe")));
        }
        var inhalt = MailLayout.Absatz($"Bonjour {konten[0].Vorname},")
            + MailLayout.Absatz("Vous avez demandé à réinitialiser votre mot de passe SKOULOU. Choisissez un nouveau mot de passe pour le compte concerné :")
            + string.Concat(bloecke)
            + MailLayout.Absatz("Ce lien est valable 24 heures. Si vous n'êtes pas à l'origine de cette demande, ignorez simplement cet e-mail.");
        await mails.EinreihenAsync(adresse, "Réinitialisation du mot de passe", inhalt, "mot-de-passe-oublie",
            vorschau: "Choisissez un nouveau mot de passe.", ct: ct);
    }

    /// <summary>Neues Passwort mit Token setzen. Fehlermeldung oder null; bei Erfolg das Identifiant.</summary>
    public async Task<(string? Fehler, string? Identifiant)> ZuruecksetzenAsync(Guid id, string? token, string? neu, string? bestaetigung)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(token)) return (MeldungUnvollstaendig, null);
        if (PruefeNeues(neu, bestaetigung) is { } fehler) return (fehler, null);
        var konto = await users.FindByIdAsync(id.ToString());
        if (konto is null || konto.Status != KontoStatus.Actif) return (MeldungUngueltigerLink, null);

        var ergebnis = await users.ResetPasswordAsync(konto, token, neu!);
        if (!ergebnis.Succeeded)
        {
            logger.LogInformation("Zurücksetzen abgelehnt: {Codes}", KontoAnlage.Fehlertext(ergebnis));
            return (ergebnis.Errors.Any(e => e.Code == "InvalidToken") ? MeldungUngueltigerLink : PasswortRegel.Pruefe(neu) ?? MeldungUngueltigerLink, null);
        }
        // Wer nach 5 Fehlversuchen gesperrt ist, greift zu « Mot de passe oublié »: mit dem Link aus seiner Mail hat er sich
        // ausgewiesen – die 15-Minuten-Sperre fällt, sonst scheitert die Anmeldung direkt nach dem Zurücksetzen.
        (konto.AccessFailedCount, konto.LockoutEnd) = (0, null);
        await NachWechselAsync(konto, AuditAktionen.PasswortZurueckgesetzt, "Mot de passe réinitialisé par lien e-mail.");
        await BestaetigungAsync(konto, "Mot de passe réinitialisé", "Le mot de passe de votre compte SKOULOU vient d'être réinitialisé.");
        return (null, konto.UserName);
    }

    private async Task NachWechselAsync(Konto konto, string aktion, string text)
    {
        konto.MussPasswortAendern = false;
        konto.AktiveSitzungId = Guid.NewGuid();
        await users.UpdateAsync(konto);
        sitzungen.Vergessen(konto.Id);
        await using var db = await dbFactory.CreateDbContextAsync();
        audit.Erfasse(db, konto.Id, konto.SchuleId, aktion, nameof(Konto), konto.Id, text);
        await db.SaveChangesAsync();
    }

    private async Task BestaetigungAsync(Konto konto, string titel, string satz)
    {
        if (!MailAusgangService.IstAdresse(konto.Email)) return;
        try
        {
            var inhalt = MailLayout.Absatz($"Bonjour {konto.Vorname},") + MailLayout.Absatz(satz)
                + MailLayout.Absatz($"Identifiant : {konto.UserName}")
                + MailLayout.Absatz("Si vous n'êtes pas à l'origine de ce changement, demandez immédiatement un nouveau mot de passe :")
                + MailLayout.Knopf(app.Value.Url("/auth/mot-de-passe-oublie"), "Mot de passe oublié");
            await mails.EinreihenAsync(konto.Email!, titel, inhalt, "mot-de-passe-confirmation");
        }
        catch (Exception ex)
        {
            // Ein Mailfehler macht die Änderung nie rückgängig.
            logger.LogWarning("Bestätigungsmail nicht eingereiht: {Meldung}", ex.Message);
        }
    }
}
