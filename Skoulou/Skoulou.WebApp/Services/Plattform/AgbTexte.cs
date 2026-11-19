using Skoulou.Core.Rechte;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Ein Abschnitt der Nutzungsbedingungen.</summary>
public sealed record AgbAbschnitt(string Id, string Titel, IReadOnlyList<string> Absaetze, IReadOnlyList<string> Punkte);

/// <summary>
/// Nutzungsbedingungen: ein einziger Text je Rollengruppe plus gemeinsame Regeln (keine Doppelfassungen).
/// Gegenüber der Alt-App bereinigt: eigener Abschnitt für das Administrateur-Konto, keine Kommission für
/// Enseignants, Vergütung des Responsable nach dem bestätigten Modell, keine automatische Sperre bei
/// unbezahlter Rechnung, keine Abonnements mehr.
/// </summary>
public static class AgbTexte
{
    public static readonly AgbAbschnitt Administration = new("administration", "Administration de la plateforme",
    [
        "Votre compte administrateur donne accès à l'ensemble des établissements, des comptes et de la facturation de SKOULOU. Utilisez ces droits uniquement pour l'exploitation de la plateforme.",
        "Chaque action sensible (validation ou suspension d'un établissement, réinitialisation d'un compte, confirmation ou annulation d'une facture) est enregistrée dans le journal d'audit.",
    ],
    [
        "Accès limité aux besoins de l'exploitation et du support.",
        "Aucune consultation des données d'élèves sans raison de service.",
        "Information de la direction avant toute suspension d'un établissement.",
    ]);

    public static readonly AgbAbschnitt Responsable = new("responsable", "Responsables SKOULOU",
    [
        "En tant que responsable SKOULOU, vous accompagnez uniquement les établissements qui vous sont attribués. Votre rôle porte sur l'accompagnement et le suivi de la facturation ; il ne vous donne pas accès aux contenus pédagogiques ni aux données des élèves.",
        "Votre rémunération est un pourcentage des factures SKOULOU de vos établissements dont le paiement a été confirmé. Le pourcentage et le montant sont figés au moment de la confirmation ; si une facture confirmée est annulée, une part négative de compensation est enregistrée. Les versements sont marqués par l'administrateur avec leur date et leur référence. Les périodes d'essai ne sont pas facturées et ne donnent donc pas de part.",
    ],
    [
        "Accès limité aux établissements attribués.",
        "Lecture seule des finances de vos établissements.",
        "Traçabilité des parts et de leurs versements.",
    ]);

    public static readonly AgbAbschnitt Direction = new("direction", "Direction et coordination",
    [
        "Vous administrez votre établissement et êtes responsable des comptes des enseignants, des élèves et des parents, de l'organisation de l'année scolaire, des notes, des bulletins et, selon vos droits, des finances.",
        "SKOULOU facture l'établissement à la fin de chaque période, selon le nombre d'élèves inscrits, le tarif par élève et par mois en vigueur au début de l'année scolaire et le nombre de mois de la période (tarif standard : 500 F CFA par élève et par mois, soit environ 5 000 F CFA pour une année scolaire complète). Ce tarif peut être adapté de façon juste à chaque établissement (par exemple selon sa taille) ; une fois l'année scolaire démarrée, il ne change plus jusqu'à sa fin. Un nouvel établissement peut bénéficier d'une période d'essai : les premières périodes de sa première année scolaire (quatre au plus), selon la décision de SKOULOU, ne sont pas facturées. Une facture impayée ne bloque pas automatiquement l'accès ; après relance, l'administrateur peut suspendre l'établissement.",
        "Contactez SKOULOU pour une difficulté technique ou une suggestion. Les demandes des élèves et des parents sont d'abord traitées par l'établissement.",
    ],
    [
        "Remise confidentielle des identifiants aux enseignants, élèves et parents.",
        "Exactitude des notes, présences et paiements enregistrés.",
        "Respect des règles locales sur les contenus et les données publiés.",
    ]);

    public static readonly AgbAbschnitt Enseignant = new("enseignant", "Enseignants",
    [
        "Votre accès est accordé par votre établissement. Vous ne voyez que les classes et les élèves de vos affectations ; protégez les informations qui vous sont confiées.",
        "Vous êtes responsable des leçons, exercices, examens, notes et appréciations que vous saisissez, ainsi que du respect des droits d'auteur et des règles de votre établissement. La direction peut consulter les contenus de l'établissement.",
    ],
    [
        "Contenus clairs, exacts et adaptés à la classe.",
        "Respect des règles d'évaluation et de confidentialité.",
        "Signalement rapide de tout incident à la direction.",
    ]);

    public static readonly AgbAbschnitt Eleve = new("eleve", "Élèves",
    [
        "SKOULOU ne conclut pas de contrat directement avec vous. Votre accès est organisé par votre établissement, avec lequel vous ou votre représentant légal êtes liés.",
        "Pour toute question sur votre compte, vos cours, vos notes ou vos frais de scolarité, adressez-vous d'abord à votre établissement.",
        "Les leçons et exercices publiés dans SKOULOU accompagnent le travail de votre classe ; ils ne remplacent ni le programme ni l'enseignement de votre établissement.",
    ],
    [
        "Accès selon la classe dans laquelle vous êtes inscrit(e).",
        "Travail personnel : ne partagez pas vos réponses d'examen.",
        "En cas de blocage, contactez d'abord la direction.",
    ]);

    public static readonly AgbAbschnitt Parent = new("parent", "Parents et tuteurs",
    [
        "Votre compte a été créé par l'établissement de votre enfant. Il vous donne accès, en lecture, aux informations des enfants que l'établissement a liés à votre compte : bulletins publiés, présences, emploi du temps, frais de scolarité et reçus.",
        "Pour toute question sur les notes, les absences ou les paiements, adressez-vous à l'établissement.",
    ],
    [
        "Accès limité aux enfants liés par l'établissement.",
        "Les données affichées sont celles enregistrées par l'établissement.",
        "En cas d'erreur, contactez la direction de l'établissement.",
    ]);

    public static readonly AgbAbschnitt Gemeinsam = new("commun", "Règles communes",
    [
        "Un compte ne peut être utilisé que sur un appareil à la fois : une nouvelle connexion met fin à la session précédente. Gardez votre mot de passe pour vous et déconnectez-vous après usage sur un appareil partagé.",
        "Les données sont utilisées uniquement pour la gestion pédagogique et administrative de l'établissement, selon les droits de chaque rôle.",
        "SKOULOU s'appuie sur des prestataires tiers, notamment pour l'hébergement. Une indisponibilité indépendante de SKOULOU, par exemple une panne d'un service Microsoft, n'ouvre pas droit à un remboursement.",
    ],
    [
        "Ne partagez jamais vos identifiants.",
        "Signalez tout usage suspect de votre compte.",
    ]);

    /// <summary>Abschnitt der Rollengruppe (genau einer je Rolle).</summary>
    public static AgbAbschnitt Fuer(string? rolle) => rolle switch
    {
        Rollen.SuperAdmin => Administration,
        Rollen.Responsable => Responsable,
        Rollen.Direktor or Rollen.Koordinator => Direction,
        Rollen.Lehrer => Enseignant,
        Rollen.Eltern => Parent,
        _ => Eleve
    };

    /// <summary>Alle Abschnitte (öffentliche Seite /conditions).</summary>
    public static readonly IReadOnlyList<AgbAbschnitt> Alle = [Direction, Enseignant, Eleve, Parent, Responsable, Administration, Gemeinsam];
}
