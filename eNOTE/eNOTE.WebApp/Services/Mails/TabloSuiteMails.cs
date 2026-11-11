using System.Globalization;
using System.Net;
using System.Text;

namespace ENote.WebApp.Services.Mails;

/// <summary>
/// Zentrale HTML-E-Mail-Vorlagen der TabloSuite-Plattform (app-übergreifend).
/// Tabellen-Layout mit Inline-Styles (Outlook, Gmail, Handy), dunkelblaues Branding, „T“-Logo,
/// Kontakt contact@tablosuite.com, Lomé/Togo. Anrede « vous » wie in der App.
/// </summary>
public static class TabloSuiteMails
{
    private const string Kontakt = "contact@tablosuite.com";
    private const string Blau = "#1e3a8a";
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    // ------------------------------------------------------------------ Konto & Zugang

    /// <summary>Willkommensmail direkt nach der Registrierung (Inscription reçue, en attente de validation).</summary>
    public static string Bienvenue(string vorname, string appName, string appUrl)
        => Wrap("Bienvenue sur TabloSuite", $"Votre inscription à {appName} est bien reçue.",
            Anrede(vorname)
            + P($"Merci pour votre inscription ! Nous avons bien reçu la création de votre compte <strong>{H(appName)}</strong>.")
            + Box("info", "Prochaine étape", "Un administrateur vérifie votre établissement. Vous recevrez un e-mail dès qu'il est validé — en général sous 24 heures.")
            + P("Votre période d'essai gratuite commence dès la validation : profitez-en pour ajouter vos prestations, votre équipe et votre logo.")
            + Knopf($"Découvrir {appName}", appUrl)
            + Gruss());

    /// <summary>Mail nach der Validierung der Registrierung durch den Admin.</summary>
    public static string InscriptionValidee(string vorname, string appName, string appUrl)
        => Wrap("Votre établissement est validé", $"Vous pouvez maintenant utiliser {appName}.",
            Anrede(vorname)
            + P($"Bonne nouvelle : votre inscription a été validée. Vous pouvez vous connecter à <strong>{H(appName)}</strong> et commencer à encaisser.")
            + Liste("Pour bien démarrer :", [
                "Ajoutez vos prestations et articles (avec prix et stock).",
                "Créez vos caissiers et leur code PIN dans « Équipe ».",
                "Ajoutez votre logo et votre slogan dans « Paramètres » pour vos reçus."])
            + Knopf("Se connecter", appUrl)
            + Gruss());

    /// <summary>Passwort-Zurücksetzen-Mail mit Link.</summary>
    public static string Reinitialisation(string pseudonym, string resetUrl)
        => Wrap("Réinitialisation du mot de passe", "Choisissez un nouveau mot de passe.",
            P($"Compte : <strong>{H(pseudonym)}</strong>")
            + P("Vous avez demandé à réinitialiser votre mot de passe. Cliquez sur le bouton ci-dessous pour en choisir un nouveau.")
            + Knopf("Choisir un nouveau mot de passe", resetUrl)
            + LinkHinweis(resetUrl)
            + Box("warn", "Vous n'êtes pas à l'origine de cette demande ?", "Ignorez simplement ce message : votre mot de passe actuel reste valable."));

    /// <summary>Hinweis nach einer Passwortänderung (Sicherheit).</summary>
    public static string PasswortGeaendert(string vorname, string pseudonym, DateTime zeitpunktUtc, string appUrl)
        => Wrap("Votre mot de passe a été modifié", "Confirmation de sécurité.",
            Anrede(vorname)
            + P($"Le mot de passe du compte <strong>{H(pseudonym)}</strong> a été modifié le {Datum(zeitpunktUtc, mitZeit: true)}.")
            + Box("warn", "Ce n'était pas vous ?", $"Utilisez « Mot de passe oublié » sur la page de connexion et prévenez-nous à {Kontakt}.")
            + Knopf("Ouvrir eNOTE", appUrl)
            + Gruss());

    /// <summary>Hinweis an die bisherige Adresse, wenn die E-Mail-Adresse des Kontos geändert wurde (Sicherheit).</summary>
    public static string EmailGeaendert(string vorname, string pseudonym, string neueAdresseMaskiert, DateTime zeitpunktUtc, string appUrl)
        => Wrap("Votre adresse e-mail a été modifiée", "Confirmation de sécurité.",
            Anrede(vorname)
            + P($"L'adresse e-mail du compte <strong>{H(pseudonym)}</strong> a été remplacée le {Datum(zeitpunktUtc, mitZeit: true)}.")
            + Tabelle([("Nouvelle adresse", neueAdresseMaskiert)])
            + P("Les prochains messages d'eNOTE (factures, mot de passe oublié) seront envoyés à la nouvelle adresse.")
            + Box("warn", "Ce n'était pas vous ?", $"Contactez-nous immédiatement à {Kontakt}.")
            + Knopf("Ouvrir eNOTE", appUrl)
            + Gruss());

    /// <summary>Vom Admin angelegtes Besitzerkonto.</summary>
    public static string KontoErstellt(string vorname, string pseudonym, string salonName, string appUrl, string? passwortLink)
        => Wrap("Votre compte eNOTE est prêt", $"Votre établissement « {salonName} » a été créé.",
            Anrede(vorname)
            + P($"L'équipe TabloSuite a créé votre compte pour l'établissement <strong>{H(salonName)}</strong>.")
            + Tabelle([("Identifiant", pseudonym), ("Établissement", salonName)])
            + (passwortLink is null
                ? P("Votre mot de passe provisoire vous a été communiqué par notre équipe. Changez-le après la première connexion (« Mon profil »).")
                  + Knopf("Se connecter", appUrl)
                : P("Pour commencer, choisissez votre mot de passe personnel :")
                  + Knopf("Choisir mon mot de passe", passwortLink)
                  + LinkHinweis(passwortLink))
            + Gruss());

    /// <summary>Willkommen für einen neuen Kassierer (ohne Passwort – das gibt der Besitzer weiter).</summary>
    public static string KassiererWillkommen(string vorname, string pseudonym, string salonName, string besitzerName, string appUrl)
        => Wrap("Bienvenue dans l'équipe", $"Vous encaissez désormais pour « {salonName} ».",
            Anrede(vorname)
            + P($"{H(besitzerName)} vous a ajouté(e) comme caissier/caissière de <strong>{H(salonName)}</strong> sur eNOTE.")
            + Tabelle([("Identifiant", pseudonym), ("Établissement", salonName)])
            + P("Votre mot de passe et votre code PIN de caisse vous sont remis par le propriétaire. Ne les communiquez à personne.")
            + Knopf("Ouvrir eNOTE", appUrl)
            + Gruss());

    // ------------------------------------------------------------------ Administration

    /// <summary>Info an das TabloSuite-Team: neues Établissement wartet auf Validierung.</summary>
    public static string NeueRegistrierungAdmin(string salonName, string besitzerName, string email, string? telefon, string? land, string adminUrl)
        => Wrap("Nouvel établissement à valider", $"{salonName} attend votre validation.",
            P("Un nouvel établissement vient de s'inscrire sur eNOTE :")
            + Tabelle([("Établissement", salonName), ("Propriétaire", besitzerName), ("E-mail", email),
                       ("Téléphone", telefon ?? "—"), ("Pays", land ?? "—")])
            + Knopf("Ouvrir l'administration", adminUrl));

    /// <summary>Info an das TabloSuite-Team: Besitzer hat eine Zahlung gemeldet.</summary>
    public static string ZahlungGemeldetAdmin(string salonName, string formel, decimal betrag, string? notiz, string adminUrl)
        => Wrap("Paiement signalé à confirmer", $"{salonName} : {Betrag(betrag)}.",
            P("Un propriétaire a signalé un paiement d'abonnement :")
            + Tabelle([("Établissement", salonName), ("Formule", formel), ("Montant", Betrag(betrag)), ("Référence / note", notiz ?? "—")])
            + P("Vérifiez la réception (Mobile Money, virement) puis confirmez ou refusez dans « Facturation ».")
            + Knopf("Ouvrir la facturation", adminUrl));

    /// <summary>Test-Mail aus der Administration.</summary>
    public static string Test(string anbieter, string absender)
        => Wrap("E-mail de test", "La configuration fonctionne.",
            P("Si vous lisez ce message, l'envoi d'e-mails d'eNOTE fonctionne.")
            + Tabelle([("Service", anbieter), ("Expéditeur", absender), ("Envoyé le", Datum(DateTime.UtcNow, mitZeit: true))]));

    // ------------------------------------------------------------------ Abonnement

    /// <summary>Neue Rechnung (Periode) für den Laden.</summary>
    public static string NouvelleFacture(string vorname, string salonName, string formel, DateTime vonUtc, DateTime bisUtc,
        decimal betrag, DateTime faelligUtc, DateTime? sperreAbUtc, string aboUrl)
        => Wrap($"Votre facture eNOTE – {Betrag(betrag)}", $"À régler avant le {Datum(faelligUtc)}.",
            Anrede(vorname)
            + P($"Voici la facture de l'abonnement eNOTE pour <strong>{H(salonName)}</strong>.")
            + Tabelle([("Formule", formel), ("Période", $"{Datum(vonUtc)} – {Datum(bisUtc)}"), ("Montant", Betrag(betrag)), ("À payer avant le", Datum(faelligUtc))], hervorheben: 2)
            + P("Payez par Mobile Money ou virement, puis signalez le paiement dans « Abonnement » : nous le confirmons rapidement.")
            + (sperreAbUtc is DateTime sperre ? Box("info", "Bon à savoir", $"Sans paiement, la caisse est suspendue à partir du {Datum(sperre)}. Vos données restent conservées.") : "")
            + Knopf("Voir et payer", aboUrl)
            + Gruss());

    /// <summary>Erinnerung kurz vor der Kassensperre.</summary>
    public static string RappelPaiement(string vorname, string salonName, decimal betrag, DateTime sperreAbUtc, string aboUrl)
        => Wrap("Rappel : paiement en attente", $"La caisse sera suspendue le {Datum(sperreAbUtc)}.",
            Anrede(vorname)
            + P($"Nous n'avons pas encore reçu le paiement de l'abonnement de <strong>{H(salonName)}</strong>.")
            + Tabelle([("Montant", Betrag(betrag)), ("Suspension de la caisse le", Datum(sperreAbUtc))], hervorheben: 0)
            + Box("warn", "Évitez l'interruption", "Payez et signalez le paiement dans « Abonnement » avant cette date. Si vous avez déjà payé, signalez-le simplement.")
            + Knopf("Signaler mon paiement", aboUrl)
            + Gruss());

    /// <summary>Kasse wurde wegen fehlender Zahlung gesperrt.</summary>
    public static string CaisseSuspendue(string vorname, string salonName, decimal betrag, string aboUrl)
        => Wrap("Votre caisse est suspendue", "Paiement de l'abonnement manquant.",
            Anrede(vorname)
            + P($"Faute de paiement, la caisse de <strong>{H(salonName)}</strong> est suspendue. Vos données, ventes et rapports restent accessibles.")
            + Tabelle([("Montant dû", Betrag(betrag))], hervorheben: 0)
            + Box("danger", "Pour réactiver la caisse", "Payez puis signalez le paiement dans « Abonnement ». La caisse est rouverte dès la confirmation.")
            + Knopf("Régulariser", aboUrl)
            + Gruss());

    /// <summary>Zahlung vom Admin bestätigt.</summary>
    public static string PaiementConfirme(string vorname, string salonName, decimal betrag, DateTime vonUtc, DateTime bisUtc, string aboUrl)
        => Wrap("Paiement confirmé – merci !", $"{Betrag(betrag)} reçus pour {salonName}.",
            Anrede(vorname)
            + P($"Nous confirmons la réception de votre paiement pour <strong>{H(salonName)}</strong>.")
            + Tabelle([("Montant", Betrag(betrag)), ("Période", $"{Datum(vonUtc)} – {Datum(bisUtc)}"), ("Statut", "Payé ✓")])
            + Knopf("Voir mon abonnement", aboUrl)
            + Gruss());

    /// <summary>Zahlung vom Admin abgelehnt (nicht gefunden).</summary>
    public static string PaiementRefuse(string vorname, string salonName, decimal betrag, string aboUrl)
        => Wrap("Paiement non retrouvé", "Merci de vérifier votre paiement.",
            Anrede(vorname)
            + P($"Nous n'avons pas pu retrouver le paiement signalé pour <strong>{H(salonName)}</strong> ({Betrag(betrag)}).")
            + Box("warn", "Que faire ?", $"Vérifiez la référence de la transaction (SMS Mobile Money) et signalez à nouveau le paiement, ou répondez à cet e-mail avec une capture du reçu.")
            + Knopf("Signaler à nouveau", aboUrl)
            + Gruss());

    /// <summary>Probezeit endet bald.</summary>
    public static string FinEssai(string vorname, string salonName, DateTime probeBisUtc, string formel, decimal preis, string aboUrl)
        => Wrap("Votre essai gratuit se termine bientôt", $"Fin de l'essai le {Datum(probeBisUtc)}.",
            Anrede(vorname)
            + P($"L'essai gratuit d'eNOTE pour <strong>{H(salonName)}</strong> se termine le <strong>{Datum(probeBisUtc)}</strong>.")
            + Tabelle([("Formule prévue", formel), ("Prix", Betrag(preis))])
            + P("Rien à faire : la formule démarre automatiquement. Vous pouvez choisir une formule plus longue (jusqu'à −10 %) dans « Abonnement ».")
            + Knopf("Choisir ma formule", aboUrl)
            + Gruss());

    /// <summary>Laden vom Admin deaktiviert oder reaktiviert.</summary>
    public static string EtablissementStatut(string vorname, string salonName, bool aktiv, string appUrl)
        => aktiv
            ? Wrap("Votre établissement est réactivé", salonName,
                Anrede(vorname) + P($"L'établissement <strong>{H(salonName)}</strong> est de nouveau actif sur eNOTE.") + Knopf("Se connecter", appUrl) + Gruss())
            : Wrap("Votre établissement est désactivé", salonName,
                Anrede(vorname) + P($"L'établissement <strong>{H(salonName)}</strong> a été désactivé par l'équipe TabloSuite.")
                + Box("info", "Une question ?", $"Répondez à cet e-mail ou écrivez-nous à {Kontakt}. Vos données sont conservées.") + Gruss());

    // ------------------------------------------------------------------ Manuelle Nachricht

    /// <summary>
    /// Freie Nachricht aus der Administration. Leerzeile = neuer Absatz, Zeilenumbruch bleibt erhalten;
    /// der Text wird vollständig HTML-maskiert (kein eingeschleustes HTML).
    /// </summary>
    public static string Nachricht(string? vorname, string titel, string text, string? knopfText, string? knopfUrl)
    {
        var absaetze = text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => P(H(a).Replace("\n", "<br/>")));
        var vorschau = text.Replace("\r", " ").Replace("\n", " ").Trim();
        var knopf = !string.IsNullOrWhiteSpace(knopfText) && !string.IsNullOrWhiteSpace(knopfUrl) ? Knopf(knopfText.Trim(), knopfUrl) : "";
        return Wrap(titel, vorschau.Length > 110 ? vorschau[..110] + "…" : vorschau,
            Anrede(vorname) + string.Concat(absaetze) + knopf + Gruss());
    }

    /// <summary>
    /// Tarifänderung: frei formulierter Text (Vorlage, vom Admin anpassbar) und der persönliche
    /// neue Monatspreis des Ladens ab dem Datum.
    /// </summary>
    public static string ChangementTarif(string vorname, string salonName, string titel, string text,
        decimal alterPreis, decimal neuerPreis, DateTime abUtc, string aboUrl)
    {
        var absaetze = text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => P(H(a).Replace("\n", "<br/>")));
        return Wrap(titel, $"Votre tarif mensuel : {Betrag(neuerPreis)}.",
            Anrede(vorname)
            + string.Concat(absaetze)
            + P($"Pour <strong>{H(salonName)}</strong> :", unten: 8)
            + Tabelle([("Tarif actuel", Betrag(alterPreis) + " / mois"), ("Nouveau tarif", Betrag(neuerPreis) + " / mois"), ("À partir du", Datum(abUtc))], hervorheben: 1)
            + Knopf("Voir mon abonnement", aboUrl)
            + Gruss());
    }

    // ------------------------------------------------------------------ Bausteine

    private static string Anrede(string? vorname)
        => P(string.IsNullOrWhiteSpace(vorname) ? "Bonjour," : $"Bonjour {H(vorname)},", unten: 4);

    private static string Gruss() => P("À bientôt,<br/>L'équipe TabloSuite", farbe: "#475569", unten: 0);

    private static string P(string html, string farbe = "#334155", int unten = 16)
        => $@"<p style=""margin:0 0 {unten}px;font-size:15px;line-height:1.6;color:{farbe};"">{html}</p>";

    private static string Knopf(string text, string url)
        => $@"<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" style=""margin:24px auto;""><tr><td style=""border-radius:10px;background:{Blau};"">
<a href=""{H(url)}"" style=""display:inline-block;padding:13px 30px;font-size:15px;font-weight:700;color:#ffffff;text-decoration:none;border-radius:10px;"">{H(text)}</a></td></tr></table>";

    private static string LinkHinweis(string url)
        => $@"<p style=""margin:0 0 16px;font-size:12px;line-height:1.5;color:#64748b;"">Si le bouton ne fonctionne pas, copiez ce lien dans votre navigateur :<br/><a href=""{H(url)}"" style=""color:#2f4ec9;word-break:break-all;"">{H(url)}</a></p>";

    private static string Box(string art, string titel, string text)
    {
        var (bg, rand, farbe) = art switch
        {
            "warn" => ("#fff7e6", "#f59e0b", "#92400e"),
            "danger" => ("#fdecea", "#dc2626", "#a1281c"),
            _ => ("#eef2ff", "#2f4ec9", "#1e3a8a")
        };
        return $@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin:0 0 16px;""><tr><td style=""background:{bg};border-left:4px solid {rand};border-radius:8px;padding:12px 14px;"">
<p style=""margin:0 0 4px;font-size:14px;font-weight:700;color:{farbe};"">{H(titel)}</p><p style=""margin:0;font-size:14px;line-height:1.5;color:#334155;"">{H(text)}</p></td></tr></table>";
    }

    private static string Tabelle(IReadOnlyList<(string Label, string Wert)> zeilen, int hervorheben = -1)
    {
        var sb = new StringBuilder(@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin:0 0 18px;border:1px solid #e2e8f0;border-radius:10px;border-collapse:separate;overflow:hidden;"">");
        for (var i = 0; i < zeilen.Count; i++)
        {
            var (label, wert) = zeilen[i];
            var rand = i < zeilen.Count - 1 ? "border-bottom:1px solid #e2e8f0;" : "";
            var stark = i == hervorheben ? "font-size:17px;font-weight:800;color:#0f172a;" : "font-size:14px;font-weight:600;color:#0f172a;";
            sb.Append($@"<tr><td style=""padding:10px 14px;{rand}font-size:13px;color:#64748b;background:#f8fafc;width:42%;"">{H(label)}</td><td style=""padding:10px 14px;{rand}{stark}"">{H(wert)}</td></tr>");
        }
        return sb.Append("</table>").ToString();
    }

    private static string Liste(string titel, IReadOnlyList<string> punkte)
    {
        var sb = new StringBuilder(P($"<strong>{H(titel)}</strong>", unten: 6));
        sb.Append(@"<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" style=""margin:0 0 16px;"">");
        foreach (var p in punkte)
            sb.Append($@"<tr><td style=""vertical-align:top;padding:2px 8px 4px 0;color:#0d9488;font-weight:800;"">✓</td><td style=""padding:2px 0 4px;font-size:15px;line-height:1.5;color:#334155;"">{H(p)}</td></tr>");
        return sb.Append("</table>").ToString();
    }

    public static string Betrag(decimal betrag) => betrag.ToString("N0", Fr) + " F CFA";

    private static string Datum(DateTime utc, bool mitZeit = false)
    {
        // Togo, Sénégal, Côte d'Ivoire, Burkina, Mali: UTC+0 (Bénin UTC+1). Perioden liegen auf Tagesgrenzen, die Uhrzeit nur bei Hinweisen.
        var d = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return mitZeit ? d.ToString("dd/MM/yyyy 'à' HH:mm", Fr) : d.ToString("dd/MM/yyyy", Fr);
    }

    private static string Wrap(string titel, string vorschau, string inhalt)
        => $@"<!DOCTYPE html>
<html lang=""fr"">
<head><meta charset=""utf-8""/><meta name=""viewport"" content=""width=device-width, initial-scale=1""/><meta name=""color-scheme"" content=""light only""/><title>{H(titel)}</title></head>
<body style=""margin:0;padding:0;background:#eef2f7;font-family:'Segoe UI',Roboto,Arial,sans-serif;-webkit-text-size-adjust:100%;"">
<div style=""display:none;max-height:0;overflow:hidden;opacity:0;"">{H(vorschau)}</div>
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#eef2f7;""><tr><td align=""center"" style=""padding:24px 12px;"">
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""max-width:560px;"">
  <tr><td style=""background:#172554;border-radius:14px 14px 0 0;padding:18px 24px;"">
    <table role=""presentation"" cellpadding=""0"" cellspacing=""0""><tr>
      <td style=""width:36px;height:36px;border-radius:9px;background:#2f4ec9;color:#ffffff;font-weight:800;font-size:18px;text-align:center;line-height:36px;"">T</td>
      <td style=""padding-left:10px;color:#ffffff;font-weight:800;font-size:17px;letter-spacing:.5px;"">TABLOSUITE <span style=""font-weight:600;color:#a5b4fc;letter-spacing:0;"">· eNOTE</span></td>
    </tr></table>
  </td></tr>
  <tr><td style=""background:#ffffff;padding:28px 24px;border-left:1px solid #e2e8f0;border-right:1px solid #e2e8f0;"">
    <h1 style=""margin:0 0 18px;font-size:21px;line-height:1.3;color:#0f172a;"">{H(titel)}</h1>
    {inhalt}
  </td></tr>
  <tr><td style=""background:#f8fafc;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 14px 14px;padding:18px 24px;font-size:12px;line-height:1.6;color:#64748b;"">
    <strong style=""color:#334155;"">TabloSuite</strong> — Votre activité, simplement connectée.<br/>
    Une question ? Répondez à cet e-mail ou écrivez à <a href=""mailto:{Kontakt}"" style=""color:#2f4ec9;text-decoration:none;"">{Kontakt}</a><br/>
    © {DateTime.UtcNow.Year} TabloSuite · Lomé, Togo
  </td></tr>
</table>
</td></tr></table>
</body>
</html>";

    private static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
