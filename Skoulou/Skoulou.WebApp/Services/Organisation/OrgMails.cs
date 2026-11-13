using System.Net;
using Microsoft.Extensions.Options;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;

namespace Skoulou.WebApp.Services.Organisation;

/// <summary>Empfänger einer Kontomail.</summary>
public sealed record MailEmpfaenger(string? Email, string Vorname, string Identifiant, string Rolle);

/// <summary>
/// Mails des Bereichs (Bauplan §11.12): Willkommen bei Kontoanlage mit Adresse, Aktivierung einer
/// Selbstregistrierung, Ablehnung. Nie Passwörter oder Tokens; ein Fehler bricht die Aktion nie ab.
/// </summary>
public sealed class OrgMails(MailAusgangService mails, IOptions<AppOptionen> app, ILogger<OrgMails> logger)
{
    public Task WillkommenAsync(MailEmpfaenger an, string schule, CancellationToken ct = default)
    {
        var rolle = Rollen.Bezeichnung(an.Rolle).ToLowerInvariant();
        var inhalt = MailLayout.Absatz($"Bonjour {an.Vorname},")
            + MailLayout.Absatz($"La direction de « {schule} » vous a créé un compte SKOULOU ({rolle}).")
            + MailLayout.InfoKasten($"<p style=\"margin:0;\">Identifiant : <strong>{WebUtility.HtmlEncode(an.Identifiant)}</strong></p>")
            + MailLayout.Absatz("Votre mot de passe de départ vous est remis par l'établissement. À la première connexion, vous choisirez votre propre mot de passe.")
            + MailLayout.Knopf(app.Value.Url("/auth/connexion"), "Se connecter à SKOULOU");
        return SendeAsync(an.Email, "Votre compte SKOULOU", inhalt, "compte-cree", ct);
    }

    public Task AktiviertAsync(MailEmpfaenger an, string schule, string klasse, CancellationToken ct = default)
    {
        var inhalt = MailLayout.Absatz($"Bonjour {an.Vorname},")
            + MailLayout.Absatz($"Bonne nouvelle : « {schule} » a validé votre inscription. Vous êtes inscrit(e) en {klasse}.")
            + MailLayout.Absatz($"Connectez-vous avec votre identifiant « {an.Identifiant} » et le mot de passe que vous avez choisi.")
            + MailLayout.Knopf(app.Value.Url("/auth/connexion"), "Se connecter à SKOULOU");
        return SendeAsync(an.Email, "Votre compte SKOULOU est activé", inhalt, "inscription-validee", ct);
    }

    public Task AbgelehntAsync(MailEmpfaenger an, string schule, string? grund, CancellationToken ct = default)
    {
        var inhalt = MailLayout.Absatz($"Bonjour {an.Vorname},")
            + MailLayout.Absatz($"« {schule} » n'a pas pu valider votre demande d'inscription sur SKOULOU.")
            + (string.IsNullOrWhiteSpace(grund) ? string.Empty : MailLayout.Absatz($"Motif indiqué : {grund}"))
            + MailLayout.Absatz("Pour toute question, adressez-vous directement à l'établissement.");
        return SendeAsync(an.Email, "Votre demande d'inscription à SKOULOU", inhalt, "inscription-refusee", ct);
    }

    private async Task SendeAsync(string? an, string titel, string inhalt, string bezug, CancellationToken ct)
    {
        if (!MailAusgangService.IstAdresse(an)) return;
        try
        {
            await mails.EinreihenAsync(an!, titel, inhalt, bezug, ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Mail « {Bezug} » nicht eingereiht: {Meldung}", bezug, ex.Message);
        }
    }
}
