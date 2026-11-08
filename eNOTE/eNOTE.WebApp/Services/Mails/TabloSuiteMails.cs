using System.Net;

namespace ENote.WebApp.Services.Mails;

/// <summary>
/// Zentrale HTML-E-Mail-Vorlagen der TabloSuite-Plattform (app-übergreifend).
/// Dunkelblaues Branding, „T"-Logo, Kontakt contact@tablosuite.com, Lomé/Togo.
/// </summary>
public static class TabloSuiteMails
{
    private const string BasisUrl = "https://enote.tablosuite.com";
    private const string Kontakt = "contact@tablosuite.com";

    /// <summary>Willkommensmail direkt nach der Registrierung (Inscription reçue, en attente de validation).</summary>
    public static string Bienvenue(string vorname, string appName, string appUrl)
        => Wrap(
            titel: "Bienvenue sur TabloSuite",
            inhalt: $@"
                <p style=""margin:0 0 4px;font-size:15px;color:#334155;"">Bonjour {H(vorname)},</p>
                <p style=""margin:0 0 16px;font-size:15px;color:#334155;line-height:1.6;"">
                    Merci pour ton inscription ! Nous avons bien reçu la création de ton compte
                    <strong>{H(appName)}</strong> sur la plateforme TabloSuite.
                </p>
                <p style=""margin:0 0 16px;font-size:15px;color:#334155;line-height:1.6;"">
                    Un administrateur va maintenant valider ton établissement. Tu recevras un e-mail
                    dès que ton compte sera activé — ensuite, tu pourras te connecter et commencer.
                </p>
                <div style=""text-align:center;margin:24px 0;"">
                    <a href=""{H(appUrl)}"" style=""display:inline-block;background:#1e3a8a;color:#fff;text-decoration:none;padding:12px 28px;border-radius:8px;font-weight:700;font-size:15px;"">Accéder à {H(appName)}</a>
                </div>
                <p style=""margin:0;font-size:14px;color:#475569;"">À bientôt,<br/>L'équipe TabloSuite</p>");

    /// <summary>Mail nach der Validierung der Registrierung durch den Admin.</summary>
    public static string InscriptionValidee(string vorname, string appName, string appUrl)
        => Wrap(
            titel: "Ton inscription a été validée",
            inhalt: $@"
                <p style=""margin:0 0 4px;font-size:15px;color:#334155;"">Bonjour {H(vorname)},</p>
                <p style=""margin:0 0 16px;font-size:15px;color:#334155;line-height:1.6;"">
                    Bonne nouvelle ! Ton inscription a été validée. Tu peux maintenant te connecter
                    à <strong>{H(appName)}</strong> et profiter de toutes les fonctionnalités.
                </p>
                <div style=""text-align:center;margin:24px 0;"">
                    <a href=""{H(appUrl)}"" style=""display:inline-block;background:#1e3a8a;color:#fff;text-decoration:none;padding:12px 28px;border-radius:8px;font-weight:700;font-size:15px;"">Se connecter</a>
                </div>
                <p style=""margin:0;font-size:14px;color:#475569;"">À bientôt,<br/>L'équipe TabloSuite</p>");

    /// <summary>Passwort-Zurücksetzen-Mail mit Link.</summary>
    public static string Reinitialisation(string pseudonym, string resetUrl)
        => Wrap(
            titel: "Réinitialisation du mot de passe",
            inhalt: $@"
                <p style=""margin:0 0 4px;font-size:15px;color:#334155;"">Compte : {H(pseudonym)}</p>
                <p style=""margin:0 0 16px;font-size:15px;color:#334155;line-height:1.6;"">
                    Tu as demandé à réinitialiser ton mot de passe. Clique sur le bouton ci-dessous
                    pour choisir un nouveau mot de passe.
                </p>
                <div style=""text-align:center;margin:24px 0;"">
                    <a href=""{H(resetUrl)}"" style=""display:inline-block;background:#1e3a8a;color:#fff;text-decoration:none;padding:12px 28px;border-radius:8px;font-weight:700;font-size:15px;"">Choisir un nouveau mot de passe</a>
                </div>
                <p style=""margin:0 0 8px;font-size:13px;color:#64748b;line-height:1.5;"">
                    Si le bouton ne fonctionne pas, copie ce lien dans ton navigateur :<br/>
                    <a href=""{H(resetUrl)}"" style=""color:#2f4ec9;word-break:break-all;"">{H(resetUrl)}</a>
                </p>
                <p style=""margin:0;font-size:14px;color:#475569;"">Si tu n'es pas à l'origine de cette demande, ignore ce message.</p>");

    private static string Wrap(string titel, string inhalt)
        => $@"<!DOCTYPE html>
<html lang=""fr"">
<head><meta charset=""utf-8""/><meta name=""viewport"" content=""width=device-width, initial-scale=1""/></head>
<body style=""margin:0;padding:0;background:#eef2f7;font-family:Segoe UI,Arial,sans-serif;"">
  <div style=""max-width:560px;margin:0 auto;padding:24px 12px;"">
    <div style=""background:#172554;border-radius:12px 12px 0 0;padding:18px 24px;display:flex;align-items:center;"">
      <span style=""display:inline-block;width:34px;height:34px;border-radius:8px;background:#2f4ec9;color:#fff;font-weight:800;font-size:18px;text-align:center;line-height:34px;"">T</span>
      <span style=""color:#fff;font-weight:800;font-size:18px;letter-spacing:.5px;margin-left:10px;"">TABLOSUITE</span>
    </div>
    <div style=""background:#ffffff;padding:28px 24px;border-left:1px solid #e2e8f0;border-right:1px solid #e2e8f0;"">
      <h1 style=""margin:0 0 18px;font-size:20px;color:#0f172a;"">{H(titel)}</h1>
      {inhalt}
    </div>
    <div style=""background:#f8fafc;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 12px 12px;padding:18px 24px;font-size:12px;color:#64748b;line-height:1.6;"">
      <strong style=""color:#334155;"">TabloSuite</strong> — Votre activité, simplement connectée.<br/>
      Une question ? Écris-nous à <a href=""mailto:{Kontakt}"" style=""color:#2f4ec9;text-decoration:none;"">{Kontakt}</a><br/>
      © {DateTime.UtcNow.Year} TabloSuite, Lomé, Togo. Tous droits réservés.<br/>
      Cet e-mail a été envoyé automatiquement, merci de ne pas y répondre directement.
    </div>
  </div>
</body>
</html>";

    private static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
