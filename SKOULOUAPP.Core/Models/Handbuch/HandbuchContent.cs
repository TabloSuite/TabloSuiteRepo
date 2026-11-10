using SkoulouApp.Core.DataModels.Identity;

namespace SkoulouApp.Core.Models.Handbuch;

public sealed record HandbuchPanel(
    string TabId,
    string Eyebrow,
    string Title,
    string Accent,
    string Link,
    string LinkLabel,
    string Summary,
    IReadOnlyList<string> Steps,
    IReadOnlyList<HandbuchUiSlice> UiSlices,
    IReadOnlyList<string> Notes);

public sealed record HandbuchUiSlice(
    string Title,
    string Description,
    IReadOnlyList<string> Highlights);

public sealed record GuideTabDefinition(string Id, string Title, string Icon);
public sealed record GuideStartStep(string Number, string Title, string Description);

public static class HandbuchContent
{
    public static IReadOnlyDictionary<string, GuideTabDefinition> TabDefinitions { get; } = new Dictionary<string, GuideTabDefinition>
    {
        ["start"] = new("start", "Démarrer", "1"),
        ["learning"] = new("learning", "Apprendre", "2"),
        ["teaching"] = new("teaching", "Pédagogie", "3"),
        ["school"] = new("school", "École", "4"),
        ["commissions"] = new("commissions", "Commissions", "5"),
        ["finance"] = new("finance", "Finances", "6"),
        ["admin"] = new("admin", "Administration", "7")
    };

    public static IReadOnlyList<GuideStartStep> GetStartSteps(string role) => role switch
    {
        Rollen.SuperAdmin =>
        [
            new("1", "Vérifier les écoles et utilisateurs", "Dans Administration, contrôlez les rôles, les affectations et les paramètres globaux."),
            new("2", "Piloter les facturations SKOULOUAPPABO", "Suivez le workflow complet: période créée, validation direction, confirmation finale après réception."),
            new("3", "Configurer prix et commissions", "Le prix global et les commissions globales se règlent ici. Une école peut aussi avoir des paramètres spécifiques."),
            new("4", "Gérer les migrations explicitement", "En développement, créez et appliquez les migrations EF manuellement avant de lancer des opérations sensibles.")
        ],
        Rollen.SchulLeiter =>
        [
            new("1", "Préparer l'école", "Créez les classes, démarrez les cycles (date + prix), puis affectez enseignants et élèves."),
            new("2", "Gérer les élèves", "Validez les inscriptions, attribuez la classe actuelle et organisez le remplissage après réouverture si besoin."),
            new("3", "Suivre paiements et clôture", "Enregistrez la scolarité, gérez l'abonnement SKOULOU et clôturez le cycle avec décision de fin."),
            new("4", "Valider les facturations", "Après clôture, contrôlez le montant, effectuez le virement puis confirmez dans la page de facturation.")
        ],
        Rollen.Koordinator =>
        [
            new("1", "Choisir l'école", "Travaillez uniquement dans les écoles attribuées."),
            new("2", "Aider à organiser les utilisateurs", "Accompagnez la direction pour les classes, enseignants, élèves et attributions."),
            new("3", "Suivre le pédagogique", "Observez la progression, les leçons partagées et les résultats, sans accéder aux finances sensibles."),
            new("4", "Respecter le périmètre", "Les actions financières et certaines décisions restent réservées à la direction ou à l'Administrateur.")
        ],
        Rollen.ProduktOwner =>
        [
            new("1", "Vérifier les écoles attribuées", "Consultez uniquement les écoles qui vous sont confiées."),
            new("2", "Suivre les commissions", "Contrôlez les périodes validées, montants du responsable produit et totaux par école."),
            new("3", "Éviter les contenus internes", "Les contenus pédagogiques internes à la direction ne sont pas votre espace de travail normal."),
            new("4", "Lire les rapports", "Utilisez les rapports pour vérifier la cohérence globale sans modifier les données scolaires.")
        ],
        Rollen.Lehrer =>
        [
            new("1", "Choisir la classe attribuée", "Dans l'espace enseignant, filtrez par école et classe autorisées."),
            new("2", "Créer ou partager le contenu", "Préparez leçons, exercices, examens et éventuellement des vidéos de leçon."),
            new("3", "Suivre et corriger", "Consultez les progrès et corrigez les réponses libres ou manuelles."),
            new("4", "Comprendre les abonnements", "Les leçons de professeur sont visibles selon l'abonnement et les règles SKOULOU.")
        ],
        _ =>
        [
            new("1", "Ouvrir mes leçons", "Commencez avec les leçons disponibles pour votre classe."),
            new("2", "Faire les exercices", "Répondez aux activités; certains exercices donnent un résultat immédiat, d'autres attendent une correction."),
            new("3", "Consulter les examens", "Vérifiez les devoirs, examens et résultats attribués."),
            new("4", "Suivre ma progression", "Utilisez le tableau de bord, les badges, XP et résultats pour voir votre avancement.")
        ]
    };

    public static string GetRoleLabel(string role) => role switch
    {
        Rollen.SuperAdmin => "l'Administrateur",
        Rollen.SchulLeiter => "la direction d'école",
        Rollen.Koordinator => "la coordination",
        Rollen.ProduktOwner => "le responsable produit",
        Rollen.Lehrer => "l'enseignant",
        _ => "l'élève"
    };

    public static IReadOnlyDictionary<string, HandbuchPanel> GetPanels(string role)
    {
        var panels = new Dictionary<string, HandbuchPanel>
        {
            ["learning"] = LearningPanel(role),
            ["teaching"] = TeachingPanel(role),
            ["school"] = SchoolPanel(role),
            ["commissions"] = CommissionsPanel(role),
            ["finance"] = FinancePanel(role),
            ["admin"] = AdminPanel(role)
        };

        return panels;
    }

    private static HandbuchPanel LearningPanel(string role) => new(
        "learning",
        "Apprendre",
        "Leçons, exercices et examens",
        "emerald",
        role == Rollen.Schueler ? "/student/lessons" : "/lektionen",
        role == Rollen.Schueler ? "Ouvrir mes leçons" : "Ouvrir le catalogue",
        "L'espace d'apprentissage regroupe les contenus standards, les leçons de professeur, les exercices et les examens. L'élève voit seulement ce qui correspond à sa classe, à son abonnement et aux attributions de son école.",
        [
            "Ouvrez « Mes leçons » dans le menu : vous y voyez les leçons de votre classe.",
            "Cliquez sur une leçon. Un cadenas indique ce qui manque (abonnement actif ou autorisation de l'école).",
            "Lisez la leçon, puis faites les exercices. Les réponses libres sont corrigées plus tard par votre enseignant.",
            "Passez les examens attribués : une question à la fois, votre résultat s'affiche à la fin.",
            "Utilisez le bouton Retour en haut de page pour revenir à la liste."
        ],
        [
            new("Tableau élève", "Vue conçue d'abord pour mobile avec cartes, progression, XP/série et accès rapide aux leçons et examens.", ["En-tête de progression", "Navigation mobile en bas", "Cartes : une action par écran"]),
            new("Lecteur de leçon", "Affiche le contenu écrit, l'exercice lié et éventuellement la vidéo de leçon.", ["Vidéo selon abonnement", "PDF sans vidéo", "Retour vers l'aperçu"]),
            new("Examens", "Mode concentration : une question à la fois, points de progression et résultat final.", ["Questions auto-corrigées", "Réponses manuelles", "Minuteur pour les examens"])
        ],
        [
            "Un abonnement SKOULOU doit exister pour accéder aux contenus concernés.",
            "Les vidéos de leçon sont stockées via le stockage média et ignorées lors des exports PDF.",
            "Les élèves voient un message lorsqu'une vidéo existe mais n'est pas incluse dans leur abonnement."
        ]);

    private static HandbuchPanel TeachingPanel(string role) => new(
        "teaching",
        "Espace pédagogique",
        "Créer, attribuer et suivre les contenus",
        "amber",
        "/teacher",
        "Ouvrir l'espace pédagogique",
        "L'espace pédagogique sert à préparer les leçons, les exercices, les examens et à suivre les élèves attribués. Les droits restent strictement limités aux écoles et classes associées à l'utilisateur.",
        [
            "Choisissez d'abord l'école et la classe dans les filtres : sans cela, aucun élève n'est affiché.",
            "Allez dans « Création leçon & examens » : onglet « Leçon + exercices » pour une leçon, onglet « Examens » pour un examen.",
            "Rédigez le contenu, ajoutez si besoin une image ou une vidéo (1 heure maximum) depuis la médiathèque, puis ajoutez les exercices.",
            "Attribuez la leçon à la classe ; partagez-la avec la direction ou autorisez l'export PDF si vous le souhaitez.",
            "Dans « Statut et progression des élèves », suivez les scores et corrigez les réponses manuelles en attente."
        ],
        [
            new("Liste élèves", "Vue de suivi avec classe, statut, progression, résultats et actions pédagogiques.", ["Filtres école/classe", "Score et progression", "Actions limitées au rôle"]),
            new("Éditeur de leçon", "Éditeur riche pour le contenu écrit; les médias doivent venir de la médiathèque autorisée.", ["Contenu écrit", "Vidéo optionnelle", "Export PDF selon droits"]),
            new("Création d'exercices", "Modèles auto-corrigés ou manuels : choix, texte, trous, audio, examens.", ["Correction automatique", "Correction manuelle", "Aperçu élève"])
        ],
        [
            "Les contenus visibles par la direction sont internes à l'école et ne sont pas destinés par défaut à l'Administrateur ni au responsable produit.",
            "Les leçons des enseignants avec activité suivent les règles de l'ensemble de leçons.",
            "Les enseignants ne gèrent pas le statut administratif des élèves."
        ]);

    private static HandbuchPanel SchoolPanel(string role) => new(
        "school",
        "Gestion de l'école",
        "Classes, utilisateurs et organisation",
        "cyan",
        "/schulleiter",
        role == Rollen.Schueler ? "Voir mon école" : "Ouvrir l'espace école",
        "Cet espace permet d'organiser la vie de l'école: classes, attributions, inscriptions et cycle pédagogique.",
        [
            "Vérifiez l'école active en haut de la page avant chaque action.",
            "Créez les classes, puis démarrez le cycle de la classe (date et prix) : c'est nécessaire avant tout abonnement.",
            "Affectez les enseignants (avec leurs heures) et les élèves aux classes, ou importez-les depuis un fichier.",
            "Validez les inscriptions en attente ; un compte non validé ne peut pas se connecter.",
            "À la fin du cycle, clôturez la classe puis promouvez les élèves admis vers la classe suivante."
        ],
        [
            new("Classes", "Organisation par classes actives avec options de cycle et préparation d'abonnement SKOULOU.", ["Création", "Attribution", "Suivi"]),
            new("Élèves", "Gestion des inscriptions, validation, blocage/déblocage et affectation de classe.", ["Validation", "Blocage", "Historique"])
        ],
        [
            "Les actions restent limitées à l'école attribuée.",
            "Les suppressions sensibles restent restreintes par la gouvernance applicative."
        ]);

    private static HandbuchPanel CommissionsPanel(string role) => new(
        "commissions",
        "Commissions",
        "Suivi des parts et validations",
        "violet",
        "/provisionen",
        "Ouvrir les commissions",
        "Consultez les montants répartis après validation des périodes de facturation SKOULOUAPPABO.",
        [
            "Ouvrir la période concernée.",
            "Vérifier le chiffre d'affaires traité.",
            "Contrôler la répartition par rôle.",
            "Comparer ouvert vs historique pour suivre les paiements réellement finalisés."
        ],
        [
            new("Vue synthèse", "Montants et parts affichés par période et par école.", ["Totaux", "Rôles", "Périodes"]),
            new("Règle importante", "Une commission déjà figée sur un abonnement payé n'est pas recalculée rétroactivement.", ["Instantané", "Traçabilité", "Cohérence"])
        ],
        [
            "Les commissions dépendent des validations en amont.",
            "Aucune modification manuelle ne doit contourner le cycle métier."
        ]);

    private static HandbuchPanel FinancePanel(string role) => new(
        "finance",
        "Finances",
        "Scolarité, abonnements et rapports",
        "rose",
        "/finanzen",
        "Ouvrir les finances",
        "L'espace finance aide à suivre la scolarité, l'abonnement SKOULOU et les périodes autorisées.",
        [
            "Vérifiez qu'un cycle est actif pour la classe concernée.",
            "Enregistrez chaque paiement (comptant ou par tranches) pour le bon élève ; « Enregistrer comme payé » active l'abonnement.",
            "Avant la clôture, contrôlez les montants ouverts : un solde impayé sera comptabilisé comme perte de l'école.",
            "Exportez le rapport financier en PDF, séparément pour chaque école."
        ],
        [
            new("Paiements", "Suivi de la scolarité et des abonnements de classe.", ["Montants", "Statuts", "Historique"]),
            new("Clôture de cycle", "La clôture peut déclencher la préparation de facturation SKOULOUAPPABO selon les règles en place.", ["Contrôle", "Validation", "Traçabilité"])
        ],
        [
            "Les écritures doivent rester cohérentes avec les périodes actives.",
            "La validation finale suit le circuit métier prévu."
        ]);

    private static HandbuchPanel AdminPanel(string role) => new(
        "admin",
        "Administration",
        "Contrôle global de la plateforme",
        "slate",
        "/admin",
        "Ouvrir l'administration",
        "Réservé aux rôles autorisés pour piloter les réglages globaux, les écoles et la supervision.",
        [
            "Contrôler les utilisateurs sensibles.",
            "Vérifier les écoles, états et journaux.",
            "Configurer le prix global et les commissions globales.",
            "Configurer si besoin des paramètres de commission spécifiques par école.",
            "N'utiliser les actions destructives qu'en contexte maîtrisé."
        ],
        [
            new("Administration centrale", "Vue transversale sur les utilisateurs, écoles et supervision.", ["Utilisateurs", "Écoles", "Journaux"]),
            new("Migrations EF", "Par défaut, appliquez les migrations explicitement par commande pour garder la maîtrise des modifications de la base de données.", ["Ajouter une migration", "Mettre à jour la base de données", "Contrôle"])
        ],
        [
            "Cet espace n'est pas destiné aux utilisateurs ordinaires.",
            "Chaque action doit rester traçable."
        ]);
}
