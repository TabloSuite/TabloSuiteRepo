using System.Net;
using Microsoft.Extensions.Options;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;

namespace Skoulou.WebApp.Services.Finanzen;

/// <summary>
/// Mails des Bereichs (nur über die Outbox, Bauplan §11.12 und §11.24): Quittung an Familie/Schüler,
/// neue Plattformrechnung und Erinnerungen an die Direktion. Ein Mailfehler bricht die Aktion nie ab.
/// </summary>
public sealed class FinanzMails(MailAusgangService mails, IOptions<AppOptionen> app, ILogger<FinanzMails> logger)
{
    public async Task<int> QuittungAsync(IEnumerable<string?> an, string vorname, string schule, string nummer, long betrag, DateOnly datum,
        string kanal, long rest, CancellationToken ct = default)
    {
        var inhalt = MailLayout.Absatz("Bonjour,")
            + MailLayout.Absatz($"« {schule} » a enregistré un paiement pour {vorname}. Merci !")
            + MailLayout.InfoKasten(
                $"<p style=\"margin:0 0 6px;\">Reçu n° <strong>{Html(nummer)}</strong> du {SkoulouKultur.Datum(datum)}</p>"
                + $"<p style=\"margin:0 0 6px;\">Montant : <strong>{Html(SkoulouKultur.Fcfa(betrag))}</strong> ({Html(kanal)})</p>"
                + $"<p style=\"margin:0;\">Reste à payer pour l'année : <strong>{Html(SkoulouKultur.Fcfa(rest))}</strong></p>")
            + MailLayout.Absatz("Le reçu est disponible dans SKOULOU, rubrique « Frais de scolarité ».")
            + MailLayout.Knopf(app.Value.Url("/auth/connexion"), "Ouvrir SKOULOU");
        var gesendet = 0;
        foreach (var adresse in an.Where(MailAusgangService.IstAdresse).Select(a => a!.Trim().ToLowerInvariant()).Distinct())
            if (await SendeAsync(adresse, $"Reçu de paiement {nummer}", inhalt, "finances-recu", ct)) gesendet++;
        return gesendet;
    }

    public async Task<int> RechnungAsync(IEnumerable<string?> an, string schule, string nummer, string periode, int anzahl, long betrag,
        DateOnly faellig, int erinnerung, CancellationToken ct = default)
    {
        var titel = erinnerung switch
        {
            0 => $"Nouvelle facture SKOULOU {nummer}",
            1 => $"Rappel : facture SKOULOU {nummer} à régler",
            _ => $"Second rappel : facture SKOULOU {nummer}"
        };
        var einleitung = erinnerung switch
        {
            0 => $"La facture SKOULOU de « {schule} » pour la période « {periode} » est disponible.",
            1 => $"La facture SKOULOU {nummer} de « {schule} » arrive à échéance aujourd'hui.",
            _ => $"La facture SKOULOU {nummer} de « {schule} » est échue depuis le {SkoulouKultur.Datum(faellig)}."
        };
        var inhalt = MailLayout.Absatz("Bonjour,")
            + MailLayout.Absatz(einleitung)
            + MailLayout.InfoKasten(
                $"<p style=\"margin:0 0 6px;\">Élèves facturés : <strong>{anzahl}</strong></p>"
                + $"<p style=\"margin:0 0 6px;\">Montant à virer : <strong>{Html(SkoulouKultur.Fcfa(betrag))}</strong></p>"
                + $"<p style=\"margin:0;\">Échéance : <strong>{SkoulouKultur.Datum(faellig)}</strong></p>")
            + MailLayout.Absatz("Après votre virement, déclarez le paiement dans SKOULOU (Finances › Facturation SKOULOU). En cas de désaccord, vous pouvez contester la facture.")
            + MailLayout.Knopf(app.Value.Url("/finances/facturation"), "Voir la facture");
        var gesendet = 0;
        foreach (var adresse in an.Where(MailAusgangService.IstAdresse).Select(a => a!.Trim().ToLowerInvariant()).Distinct())
            if (await SendeAsync(adresse, titel, inhalt, erinnerung == 0 ? "facture-skoulou" : "facture-skoulou-rappel", ct)) gesendet++;
        return gesendet;
    }

    private static string Html(string text) => WebUtility.HtmlEncode(text);

    private async Task<bool> SendeAsync(string an, string titel, string inhalt, string bezug, CancellationToken ct)
    {
        try
        {
            return await mails.EinreihenAsync(an, titel, inhalt, bezug, ct: ct) is not null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Mail « {Bezug} » nicht eingereiht.", bezug);
            return false;
        }
    }
}
