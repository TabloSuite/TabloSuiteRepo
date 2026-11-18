using System.Net;
using Microsoft.Extensions.Options;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>
/// Mails des Bereichs Plattform (Registrierung, Validierung, Suspendierung, Responsables). Alle laufen über
/// die Outbox mit Präfix « [SKOULOU] »; nie Passwörter oder Tokens; ein Fehler bricht die Aktion nie ab.
/// </summary>
public sealed class PlattformMails(MailAusgangService mails, IOptions<AppOptionen> app, ILogger<PlattformMails> logger)
{
    private string Anmeldung => app.Value.Url("/auth/connexion");

    private static string Fett(string text) => $"<strong>{WebUtility.HtmlEncode(text)}</strong>";

    public Task SchuleEingegangenAsync(string? an, string vorname, string identifiant, string schule, CancellationToken ct = default)
        => SendeAsync(an, "Votre établissement est enregistré", MailLayout.Absatz($"Bonjour {vorname},")
            + MailLayout.Absatz($"Merci d'avoir inscrit « {schule} » sur SKOULOU. Votre demande est en cours de vérification par notre équipe.")
            + MailLayout.InfoKasten($"<p style=\"margin:0;\">Votre identifiant de connexion : {Fett(identifiant)}</p>")
            + MailLayout.Absatz("Vous recevrez un e-mail dès la validation. Vous pourrez alors ajouter votre équipe, vos classes et vos élèves.")
            + MailLayout.Knopf(Anmeldung, "Suivre ma demande"), "inscription-ecole", ct);

    public Task NeueSchuleAnAdminAsync(string an, string schule, string ort, string direktor, CancellationToken ct = default)
        => SendeAsync(an, $"Nouvel établissement à valider : {schule}", MailLayout.Absatz("Bonjour,")
            + MailLayout.Absatz($"Un nouvel établissement vient de s'inscrire : « {schule} » ({ort}), direction : {direktor}.")
            + MailLayout.Absatz("Après la validation, décidez de sa période d'essai : « Écoles » › « Essai » (par défaut, aucune).")
            + MailLayout.Knopf(app.Value.Url("/admin/ecoles?filtre=en-attente"), "Vérifier et valider"), "admin-nouvelle-ecole", ct);

    public Task SchuleValidiertAsync(string? an, string vorname, string schule, CancellationToken ct = default)
        => SendeAsync(an, "Votre établissement est validé", MailLayout.Absatz($"Bonjour {vorname},")
            + MailLayout.Absatz($"Bonne nouvelle : « {schule} » est validé sur SKOULOU. Vous pouvez maintenant préparer l'année scolaire, ajouter votre équipe et vos élèves.")
            + MailLayout.Knopf(Anmeldung, "Se connecter à SKOULOU"), "ecole-validee", ct);

    public Task SchuleAbgelehntAsync(string? an, string vorname, string schule, string grund, CancellationToken ct = default)
        => SendeAsync(an, "Votre demande d'inscription à SKOULOU", MailLayout.Absatz($"Bonjour {vorname},")
            + MailLayout.Absatz($"Nous n'avons pas pu valider l'inscription de « {schule} » sur SKOULOU.")
            + MailLayout.Absatz($"Motif : {grund}")
            + MailLayout.Absatz("Pour toute question, répondez simplement à cet e-mail ou écrivez-nous depuis la page Contact."), "ecole-refusee", ct);

    public Task SchuleSuspendiertAsync(string? an, string vorname, string schule, string grund, CancellationToken ct = default)
        => SendeAsync(an, "Accès de votre établissement suspendu", MailLayout.Absatz($"Bonjour {vorname},")
            + MailLayout.Absatz($"L'accès de « {schule} » à SKOULOU est suspendu. Les comptes de l'établissement ne peuvent plus se connecter ; aucune donnée n'est supprimée.")
            + MailLayout.Absatz($"Motif : {grund}")
            + MailLayout.Absatz("Pour régulariser la situation, contactez-nous en répondant à cet e-mail."), "ecole-suspendue", ct);

    public Task SchuleReaktiviertAsync(string? an, string vorname, string schule, CancellationToken ct = default)
        => SendeAsync(an, "Accès de votre établissement rétabli", MailLayout.Absatz($"Bonjour {vorname},")
            + MailLayout.Absatz($"L'accès de « {schule} » à SKOULOU est de nouveau ouvert. Toute l'équipe peut se reconnecter.")
            + MailLayout.Knopf(Anmeldung, "Se connecter à SKOULOU"), "ecole-reactivee", ct);

    public Task SchuelerEingegangenAsync(string? an, string vorname, string identifiant, string schule, CancellationToken ct = default)
        => SendeAsync(an, "Votre inscription est enregistrée", MailLayout.Absatz($"Bonjour {vorname},")
            + MailLayout.Absatz($"Votre demande d'inscription à « {schule} » est enregistrée. La direction de l'établissement doit maintenant la valider.")
            + MailLayout.InfoKasten($"<p style=\"margin:0;\">Votre identifiant de connexion : {Fett(identifiant)}</p>")
            + MailLayout.Absatz("Vous recevrez un e-mail dès la validation, puis vous pourrez vous connecter avec le mot de passe que vous avez choisi."), "inscription-eleve", ct);

    public Task ResponsableWillkommenAsync(string? an, string vorname, string identifiant, CancellationToken ct = default)
        => SendeAsync(an, "Votre accès Responsable SKOULOU", MailLayout.Absatz($"Bonjour {vorname},")
            + MailLayout.Absatz("Un compte Responsable SKOULOU vient d'être créé pour vous. Vous accompagnez les établissements qui vous sont attribués.")
            + MailLayout.InfoKasten($"<p style=\"margin:0;\">Votre identifiant de connexion : {Fett(identifiant)}</p>")
            + MailLayout.Absatz("Votre mot de passe de départ vous est remis personnellement. À la première connexion, vous choisirez votre propre mot de passe.")
            + MailLayout.Knopf(Anmeldung, "Se connecter à SKOULOU"), "responsable-cree", ct);

    /// <summary>Freie Nachricht der Messagerie (Text bereits als sicheres HTML).</summary>
    /// <summary>18.11.2026: Ein Responsable hat die Probezeit einer seiner Schulen geändert (Meldung an jeden Admin).</summary>
    public Task EssaiGeaendertAnAdminAsync(string an, string schule, string responsable, string vorher, string nachher, CancellationToken ct = default)
        => SendeAsync(an, $"Période d'essai modifiée : {schule}", MailLayout.Absatz("Bonjour,")
            + MailLayout.Absatz($"{Fett(responsable)} a modifié la période d'essai de « {schule} » : {vorher} → {Fett(nachher)}.")
            + MailLayout.Absatz("Vous pouvez la corriger tant que l'établissement n'a pas encore de facture SKOULOU.")
            + MailLayout.Knopf(app.Value.Url("/admin/ecoles"), "Voir les écoles"), "admin-essai", ct);

    public Task<Guid?> NachrichtAsync(string an, string betreff, string textHtml, CancellationToken ct = default)
        => mails.EinreihenAsync(an, betreff, $"<div style=\"margin:0 0 14px;\">{textHtml}</div>", "messagerie", ct: ct);

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
