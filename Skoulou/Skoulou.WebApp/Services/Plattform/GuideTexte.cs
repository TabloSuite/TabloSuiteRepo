using Skoulou.Core.Rechte;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Ein Schritt des Guides (kurz, mit Link auf die Seite).</summary>
public sealed record GuideSchritt(string Titel, string Text, string? Href = null);

/// <summary>Kapitel des Guides.</summary>
public sealed record GuideKapitel(string Id, string Titel, string Icon, IReadOnlyList<GuideSchritt> Schritte);

/// <summary>
/// Guide utilisateur (/aide/guide): kurze, rollenbezogene Anleitungen mit Links. Eine Quelle je Rolle
/// (Hauptrolle = einzige Rolle des Kontos); « Mon compte » gilt für alle.
/// </summary>
public static class GuideTexte
{
    private static readonly GuideKapitel Konto = new("compte", "Mon compte", "user",
    [
        new("Se connecter", "Connectez-vous avec votre identifiant (ex. ama.mensah) et votre mot de passe. « Rester connecté » évite de ressaisir vos données sur votre propre appareil ; sur un ordinateur partagé, déconnectez-vous toujours.", "/auth/connexion"),
        new("Changer de mot de passe", "Depuis votre profil, rubrique « Mot de passe ». Sans adresse e-mail, demandez un nouveau mot de passe de départ à la direction de votre établissement.", "/profil"),
        new("Mon profil", "Nom, téléphone, thème clair ou sombre. Votre identifiant et votre adresse e-mail sont gérés par l'établissement.", "/profil"),
        new("Besoin d'aide ?", "Regardez les vidéos d'aide ou écrivez à l'équipe SKOULOU depuis la page Contact.", "/aide/videos")
    ]);

    private static readonly IReadOnlyList<GuideKapitel> Admin =
    [
        new("ecoles", "Établissements", "school",
        [
            new("Valider une inscription", "Les nouvelles écoles arrivent « À valider ». Vérifiez le nom et la direction, puis validez : la direction reçoit un e-mail et peut se connecter. En cas de refus, indiquez un motif clair.", "/admin/ecoles"),
            new("Suspendre ou réactiver", "La suspension bloque la connexion de tous les comptes de l'école, sans rien supprimer. Le motif est envoyé à la direction.", "/admin/ecoles"),
            new("Responsable", "Attribuez à chaque école un responsable SKOULOU : il voit ses écoles, leur facturation et leurs messages.", "/admin/responsables")
        ]),
        new("comptes", "Comptes", "users",
        [
            new("Rechercher un compte", "Recherchez par nom, identifiant ou e-mail ; filtrez par rôle. Les numéros de téléphone privés ne sont jamais affichés.", "/admin/comptes"),
            new("Bloquer, débloquer, mot de passe", "Bloquer met fin aux sessions immédiatement. « Nouveau mot de passe » affiche un mot de passe de départ une seule fois : transmettez-le personnellement.", "/admin/comptes")
        ]),
        new("communication", "Communication", "megaphone",
        [
            new("Annonces", "Choisissez le public (y compris les visiteurs du site), éventuellement une école, et la période d'affichage. « Important » met l'annonce en avant.", "/admin/annonces"),
            new("Messagerie", "Les e-mails reçus sur contact@tablosuite.com arrivent ici. Attribuez-les à une école pour que son responsable les voie ; répondez directement.", "/admin/messagerie")
        ]),
        new("surveillance", "Surveillance", "shield",
        [
            new("Journal des erreurs", "Erreurs techniques des 90 derniers jours. Marquez-les « traitées » une fois corrigées.", "/admin/journal"),
            new("Audit", "Toutes les actions sensibles (validations, blocages, mots de passe, notes, paiements). Conservé sans limite.", "/admin/audit"),
            new("Médias", "Espace occupé par école ; les fichiers non utilisés peuvent être supprimés.", "/admin/medias")
        ])
    ];

    private static readonly IReadOnlyList<GuideKapitel> Responsable =
    [
        new("ecoles", "Mes écoles", "school",
        [
            new("Suivre vos écoles", "Statut, nombre d'élèves, inscriptions à valider et contacts de la direction de chaque école.", "/responsable"),
            new("Facturation", "Factures SKOULOU de vos écoles et paiements déclarés.", "/responsable/facturation"),
            new("Messagerie", "Messages de vos écoles ; vous pouvez répondre et écrire à leur direction.", "/responsable/messagerie")
        ])
    ];

    private static readonly IReadOnlyList<GuideKapitel> Direction =
    [
        new("demarrer", "Démarrer l'année", "check-square",
        [
            new("1. Année scolaire et périodes", "Créez l'année (ex. 2026-2027) et ses trimestres, semestres ou sessions.", "/direction/annee"),
            new("2. Matières", "Listez les matières enseignées avec leur groupe (lettres, sciences…).", "/direction/matieres"),
            new("3. Classes", "Créez les classes, leurs coefficients et les enseignants de chaque matière.", "/direction/classes"),
            new("4. Équipe", "Ajoutez enseignants et coordinateurs ; imprimez la liste des accès pour ceux sans e-mail.", "/direction/equipe"),
            new("5. Élèves et parents", "Ajoutez ou importez les élèves (Excel) dans leur classe, puis liez les parents. Validez aussi les inscriptions en ligne.", "/direction/eleves")
        ]),
        new("quotidien", "Au quotidien", "calendar",
        [
            new("Emplois du temps", "Construisez l'emploi du temps de chaque classe ; les conflits d'enseignant ou de salle sont signalés.", "/direction/emplois-du-temps"),
            new("Présences", "Faites l'appel par classe : tout le monde est présent par défaut, cochez les absences et retards.", "/presences"),
            new("Notes et bulletins", "Saisissez les évaluations dans la grille, préparez le conseil de classe, puis publiez les bulletins (PDF avec code QR).", "/bulletins")
        ]),
        new("finances", "Finances", "wallet",
        [
            new("Frais et paiements", "Définissez les frais par niveau, enregistrez les paiements et imprimez les reçus.", "/finances"),
            new("Facture SKOULOU", "Consultez la facture de chaque période et déclarez votre paiement.", "/finances/facturation")
        ])
    ];

    private static readonly IReadOnlyList<GuideKapitel> Enseignant =
    [
        new("cours", "Mes cours", "book-open",
        [
            new("Leçons et exercices", "Créez une leçon avec ses pages et ses questions, puis attribuez-la à une classe.", "/enseignant/lecons"),
            new("Examens", "Préparez un examen chronométré ; les élèves le passent une seule fois dans la période choisie.", "/enseignant/examens"),
            new("Corrections", "Corrigez les réponses libres ; les autres questions sont notées automatiquement.", "/enseignant/corrections")
        ]),
        new("classe", "Ma classe", "users",
        [
            new("Appel", "Faites l'appel de vos classes chaque jour.", "/presences"),
            new("Notes", "Saisissez les notes dans la grille : « abs » absent, « aj » absence justifiée, « disp » dispensé.", "/notes"),
            new("Emploi du temps", "Votre semaine, classe par classe.", "/enseignant/emploi-du-temps")
        ])
    ];

    private static readonly IReadOnlyList<GuideKapitel> Eleve =
    [
        new("apprendre", "Apprendre", "book-open",
        [
            new("Leçons", "Lisez les leçons de votre classe et faites les exercices ; votre meilleur essai compte.", "/eleve/lecons"),
            new("Examens", "Un examen se passe une seule fois, dans le temps indiqué. Vos réponses sont envoyées automatiquement à la fin.", "/eleve/examens"),
            new("Résultats et bulletins", "Vos résultats d'exercices et vos bulletins publiés.", "/eleve/resultats")
        ]),
        new("ecole", "Mon école", "school",
        [
            new("Emploi du temps", "Vos cours de la semaine.", "/eleve/emploi-du-temps"),
            new("Frais de scolarité", "Ce qui est payé et ce qui reste à payer.", "/eleve/frais")
        ])
    ];

    private static readonly IReadOnlyList<GuideKapitel> Parent =
    [
        new("enfants", "Mes enfants", "famille",
        [
            new("Choisir un enfant", "Si plusieurs enfants sont liés à votre compte, choisissez-le en haut de chaque page.", "/parents"),
            new("Notes et bulletins", "Bulletins publiés par l'établissement, à télécharger en PDF.", "/parents/bulletins"),
            new("Présences", "Absences et retards de votre enfant.", "/parents/presences"),
            new("Frais et reçus", "Paiements, reste à payer et reçus.", "/parents/frais")
        ])
    ];

    /// <summary>Kapitel für eine Rolle; « Mon compte » steht immer am Ende.</summary>
    public static IReadOnlyList<GuideKapitel> Fuer(string? rolle)
    {
        IReadOnlyList<GuideKapitel> eigene = rolle switch
        {
            Rollen.SuperAdmin => Admin,
            Rollen.Responsable => Responsable,
            Rollen.Direktor or Rollen.Koordinator => Direction,
            Rollen.Lehrer => Enseignant,
            Rollen.Schueler => Eleve,
            Rollen.Eltern => Parent,
            _ => []
        };
        return [.. eigene, Konto];
    }
}
