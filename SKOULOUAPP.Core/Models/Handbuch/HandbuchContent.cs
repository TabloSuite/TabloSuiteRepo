using HalloApp.Core.DataModels.Identity;

namespace HalloApp.Core.Models.Handbuch;

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
            new("1", "Vérifier les écoles et utilisateurs", "Dans Administration, contrôle les rôles, les affectations et les paramètres globaux."),
            new("2", "Piloter les facturations HALLOAPP", "Suis le workflow complet: période créée, validation direction, confirmation finale après réception."),
            new("3", "Configurer prix et commissions", "Le prix global et les commissions globales se règlent ici. Une école peut aussi avoir des paramètres spécifiques."),
            new("4", "Gérer les migrations explicitement", "En développement, crée et applique les migrations EF manuellement avant de lancer des opérations sensibles.")
        ],
        Rollen.SchulLeiter =>
        [
            new("1", "Préparer l'école", "Crée les classes, démarre les cycles (date + prix), puis affecte enseignants et élèves."),
            new("2", "Gérer les élèves", "Valide les inscriptions, attribue la classe actuelle et organise le remplissage après réouverture si besoin."),
            new("3", "Suivre paiements et clôture", "Enregistre la scolarité, gère l'abonnement HALLOAPP et clôture le cycle avec décision de fin."),
            new("4", "Valider les facturations", "Après clôture, contrôle le montant, effectue le virement puis confirme dans la page de facturation.")
        ],
        Rollen.Koordinator =>
        [
            new("1", "Choisir l'école", "Travaille uniquement dans les écoles attribuées."),
            new("2", "Aider à organiser les utilisateurs", "Accompagne la direction pour les classes, enseignants, élèves et attributions."),
            new("3", "Suivre le pédagogique", "Observe la progression, les leçons partagées et les résultats, sans accéder aux finances sensibles."),
            new("4", "Respecter le périmètre", "Les actions financières et certaines décisions restent réservées à la direction ou à l'Administrateur.")
        ],
        Rollen.ProduktOwner =>
        [
            new("1", "Vérifier les écoles attribuées", "Consulte uniquement les écoles qui te sont confiées."),
            new("2", "Suivre les commissions", "Contrôle les périodes validées, montants du responsable produit et totaux par école."),
            new("3", "Éviter les contenus internes", "Les contenus pédagogiques internes à la direction ne sont pas ton espace de travail normal."),
            new("4", "Lire les rapports", "Utilise les rapports pour vérifier la cohérence globale sans modifier les données scolaires.")
        ],
        Rollen.Lehrer =>
        [
            new("1", "Choisir la classe attribuée", "Dans l'espace enseignant, filtre par école et classe autorisées."),
            new("2", "Créer ou partager le contenu", "Prépare leçons, exercices, examens et éventuellement des vidéos de leçon."),
            new("3", "Suivre et corriger", "Consulte les progrès et corrige les réponses libres ou manuelles."),
            new("4", "Comprendre les abonnements", "Les leçons de professeur sont visibles selon l'abonnement et les règles HALLOAPP.")
        ],
        _ =>
        [
            new("1", "Ouvrir mes leçons", "Commence avec les leçons disponibles pour ta classe."),
            new("2", "Faire les exercices", "Réponds aux activités; certains exercices donnent un résultat immédiat, d'autres attendent une correction."),
            new("3", "Consulter les examens", "Vérifie les devoirs, examens et résultats attribués."),
            new("4", "Suivre ma progression", "Utilise le tableau de bord, les badges, XP et résultats pour voir ton avancement.")
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
            "Ouvrir l'espace élève ou le catalogue selon ton rôle.",
            "Choisir une leçon ou un parcours; les contenus verrouillés indiquent la condition manquante.",
            "Traiter les exercices; les réponses libres peuvent attendre une correction manuelle.",
            "Consulter les examens et résultats dans les cartes dédiées.",
            "Utiliser les boutons retour dans les pages internes pour revenir à la bonne vue."
        ],
        [
            new("Tableau élève", "Vue conçue d'abord pour mobile avec cartes, progression, XP/série et accès rapide aux leçons et examens.", ["En-tête de progression", "Navigation mobile en bas", "Cartes : une action par écran"]),
            new("Lecteur de leçon", "Affiche le contenu écrit, l'exercice lié et éventuellement la vidéo de leçon.", ["Vidéo selon abonnement", "PDF sans vidéo", "Retour vers l'aperçu"]),
            new("Examens", "Mode concentration : une question à la fois, points de progression et résultat final.", ["Questions auto-corrigées", "Réponses manuelles", "Minuteur pour les examens"])
        ],
        [
            "Un abonnement HALLOAPP doit exister pour accéder aux contenus concernés.",
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
            "Filtrer par école et classe avant d'agir.",
            "Créer une leçon professeur, avec contenu écrit et éventuellement vidéo de maximum une heure.",
            "Associer une activité ou un examen selon le besoin pédagogique.",
            "Partager avec les élèves ou la direction selon les options visibles.",
            "Corriger les réponses manuelles et suivre les scores."
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
            "Vérifier l'école active avant chaque action.",
            "Créer ou mettre à jour les classes selon l'organisation réelle.",
            "Attribuer correctement les enseignants et les élèves.",
            "Valider les inscriptions en attente lorsque le dossier est complet.",
            "Utiliser la réaffectation des élèves après réouverture de classe si nécessaire."
        ],
        [
            new("Classes", "Organisation par classes actives avec options de cycle et préparation d'abonnement HALLOAPP.", ["Création", "Attribution", "Suivi"]),
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
        "Consulte les montants répartis après validation des périodes de facturation HALLOAPP.",
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
        "L'espace finance aide à suivre la scolarité, l'abonnement HALLOAPP et les périodes autorisées.",
        [
            "Vérifier le cycle actif.",
            "Enregistrer les paiements au bon élève et à la bonne classe.",
            "Contrôler les rapports avant clôture.",
            "Pour la direction : suivre le solde actuel et le solde théorique."
        ],
        [
            new("Paiements", "Suivi de la scolarité et des abonnements de classe.", ["Montants", "Statuts", "Historique"]),
            new("Clôture de cycle", "La clôture peut déclencher la préparation de facturation HALLOAPP selon les règles en place.", ["Contrôle", "Validation", "Traçabilité"])
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
            new("Migrations EF", "Par défaut, applique les migrations explicitement par commande pour garder la maîtrise des modifications de la base de données.", ["Ajouter une migration", "Mettre à jour la base de données", "Contrôle"])
        ],
        [
            "Cet espace n'est pas destiné aux utilisateurs ordinaires.",
            "Chaque action doit rester traçable."
        ]);
}
