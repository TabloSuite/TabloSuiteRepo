namespace ENote.WebApp.Services.Mails;

/// <summary>
/// Katalog aller E-Mail-Arten (Kategorie = Postmark-Tag). <see cref="MailArt.Pflicht"/>-Mails gehen immer
/// raus (Zugang, Rechnung, Sperre); alle anderen lassen sich in Administration → E-mails abschalten –
/// wichtig, solange der Postmark-Plan nur wenige Mails pro Monat erlaubt.
/// </summary>
public static class MailArten
{
    public sealed record MailArt(string Kategorie, string Name, string Empfaenger, string Wann, bool Pflicht, bool StandardAktiv);

    public static readonly IReadOnlyList<MailArt> Alle =
    [
        // Immer – ohne diese Mails kommen Nutzer nicht an ihr Konto oder verpassen eine Zahlung.
        new("reinitialisation", "Mot de passe oublié", "Utilisateur", "Sur demande, depuis la page de connexion", true, true),
        new("compte-cree", "Compte créé par l'admin", "Propriétaire", "Quand l'admin ajoute un propriétaire", true, true),
        new("inscription-validee", "Établissement validé", "Propriétaire", "Quand l'admin valide l'inscription", true, true),
        new("facture", "Nouvelle facture", "Propriétaire", "Dès qu'une période d'abonnement est facturée", true, true),
        new("caisse-suspendue", "Caisse suspendue", "Propriétaire", "Le jour de la suspension pour impayé", true, true),
        new("paiement-refuse", "Paiement non retrouvé", "Propriétaire", "Quand l'admin refuse un paiement signalé", true, true),
        new("changement-tarif", "Changement de tarif", "Propriétaire", "Quand l'admin modifie le tarif général (si coché)", true, true),

        // Abschaltbar – standardmäßig an
        new("rappel-paiement", "Rappel de paiement", "Propriétaire", $"{MailBenachrichtigungen.ErinnerungTageVorSperre} jours avant la suspension", false, true),
        new("inscription-admin", "Nouvel établissement", "Équipe TabloSuite", "À chaque nouvelle inscription", false, true),
        new("paiement-signale", "Paiement signalé", "Équipe TabloSuite", "Quand un propriétaire signale un paiement", false, true),
        new("etablissement-statut", "Établissement désactivé / réactivé", "Propriétaire", "Quand l'admin change le statut", false, true),
        new("email-modifiee", "Adresse e-mail modifiée", "Utilisateur (ancienne adresse)", "Quand l'adresse e-mail du compte change (sécurité)", false, true),

        // Abschaltbar – standardmäßig aus (später bei größerem Mail-Kontingent aktivieren)
        new("bienvenue", "Bienvenue", "Propriétaire", "Juste après l'inscription", false, false),
        new("paiement-confirme", "Paiement confirmé", "Propriétaire", "Quand l'admin confirme un paiement", false, false),
        new("caissier-bienvenue", "Nouveau caissier", "Caissier", "Quand le propriétaire crée un caissier", false, false),
        new("mot-de-passe", "Mot de passe modifié", "Utilisateur", "Après chaque changement de mot de passe", false, false),
        new("fin-essai", "Fin de l'essai", "Propriétaire", $"{MailBenachrichtigungen.ProbeHinweisTage} jours avant la fin de l'essai", false, false),
    ];

    /// <summary>Manuelle Nachrichten und Test-Mails – immer erlaubt, nicht im Schalter-Katalog.</summary>
    public static readonly IReadOnlySet<string> Manuell = new HashSet<string> { "test", "message", "annonce" };

    public static MailArt? Finde(string? kategorie) => Alle.FirstOrDefault(a => a.Kategorie == kategorie);
}
