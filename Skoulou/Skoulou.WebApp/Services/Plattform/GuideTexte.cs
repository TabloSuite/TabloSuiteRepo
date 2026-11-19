using Skoulou.Core.Rechte;
using Skoulou.WebApp.Components.Layout;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Ein Schritt des Guides (kurz, mit Link auf die Seite).</summary>
public sealed record GuideSchritt(string Titel, string Text, string? Href = null);

/// <summary>Kapitel des Guides.</summary>
public sealed record GuideKapitel(string Id, string Titel, string Icon, IReadOnlyList<GuideSchritt> Schritte);

/// <summary>
/// Guide utilisateur (/aide/guide): kurze, rollenbezogene Anleitungen mit Links. Eine Quelle je Rolle
/// (Hauptrolle = einzige Rolle des Kontos); « Mon compte » gilt für alle. Schritte, deren Seite die Person nicht
/// erreicht (Koordinator ohne Finanzrecht, Administrateurs nur für den Hauptadministrator), entfallen – die Navigation
/// (<see cref="NavKarte"/>) ist die Quelle der erreichbaren Seiten.
/// </summary>
public static class GuideTexte
{
    /// <summary>Seiten, die jede angemeldete Person erreicht (außerhalb der Rollen-Navigation).</summary>
    private static readonly string[] Allgemein = ["/profil", "/aide/videos", "/aide/guide", "/contact"];

    private static readonly GuideKapitel Konto = new("compte", "Mon compte", "user",
    [
        new("Se connecter", "Connectez-vous avec votre identifiant (ex. ama.mensah) et votre mot de passe. « Rester connecté » évite de ressaisir vos données sur votre propre appareil ; sur un ordinateur partagé, déconnectez-vous toujours (menu en haut à droite)."),
        new("Changer de mot de passe", "Dans votre profil, carte « Changer le mot de passe ». Mot de passe oublié : utilisez le lien de la page de connexion si votre compte a une adresse e-mail ; sinon, demandez un nouveau mot de passe de départ à la direction de votre établissement.", "/profil"),
        new("Mon profil", "Prénom, nom, téléphone, genre et thème clair ou sombre. Votre identifiant et votre adresse e-mail ne se modifient pas ici.", "/profil"),
        new("Besoin d'aide ?", "Regardez les vidéos d'aide ou écrivez à l'équipe SKOULOU depuis la page Contact.", "/aide/videos")
    ]);

    private static readonly IReadOnlyList<GuideKapitel> Admin =
    [
        new("ecoles", "Établissements", "school",
        [
            new("Valider une inscription", "Les nouvelles écoles arrivent « À valider ». Vérifiez le nom et la direction, puis validez : la direction reçoit un e-mail et peut se connecter. En cas de refus, indiquez un motif clair.", "/admin/ecoles"),
            new("Suspendre ou réactiver", "La suspension bloque la connexion de tous les comptes de l'école, sans rien supprimer. Le motif est envoyé à la direction.", "/admin/ecoles"),
            new("Responsables", "Créez les comptes des responsables SKOULOU, puis attribuez une école à chacun depuis la page Écoles. Ils suivent leurs écoles, leur facturation et leurs messages, sans rien pouvoir supprimer ni bloquer.", "/admin/responsables")
        ]),
        new("comptes", "Comptes", "users",
        [
            new("Rechercher un compte", "Recherchez par nom, identifiant ou e-mail ; filtrez par rôle.", "/admin/comptes"),
            new("Bloquer, débloquer, mot de passe", "Bloquer met fin aux sessions immédiatement. « Nouveau mot de passe » affiche un mot de passe de départ une seule fois : transmettez-le personnellement.", "/admin/comptes"),
            new("Administrateurs", "Réservé à l'administrateur principal : ajoutez, modifiez, bloquez ou supprimez les autres administrateurs. Le mot de passe de départ s'affiche une seule fois ; transmettez-le personnellement.", "/admin/administrateurs")
        ]),
        new("facturation", "Facturation", "receipt",
        [
            new("Factures SKOULOU", "Une facture par école et par période. Vérifiez chaque paiement déclaré avant de le confirmer, traitez les contestations et versez les parts des responsables. « Facturer maintenant » crée sans attendre les factures des périodes terminées.", "/admin/facturation"),
            new("Tarifs", "Prix mensuel par élève, délai de paiement et part des responsables. Le prix est figé pour chaque école au démarrage de son année scolaire.", "/admin/tarifs")
        ]),
        new("communication", "Communication", "megaphone",
        [
            new("Annonces", "Choisissez le public (y compris les visiteurs du site), éventuellement une école, et la période d'affichage. « Important » met l'annonce en avant.", "/admin/annonces"),
            new("Messagerie", "Les e-mails reçus sur contact@tablosuite.com arrivent ici. Attribuez-les à une école pour que son responsable les voie ; répondez directement.", "/admin/messagerie")
        ]),
        new("surveillance", "Surveillance", "shield",
        [
            new("Journal des erreurs", "Erreurs techniques des 90 derniers jours. Marquez-les « Traitée » une fois corrigées.", "/admin/journal"),
            new("Audit", "Toutes les actions sensibles (validations, blocages, mots de passe, notes, paiements). Conservé sans limite.", "/admin/audit"),
            new("Médias", "Espace occupé par école ; seuls les fichiers non utilisés peuvent être supprimés.", "/admin/medias")
        ]),
        new("formation", "Formation", "book",
        [
            new("Formation des responsables", "Chaque responsable lit ses leçons et passe une évaluation à intervalle régulier (3 essais, le dernier compte). Vous voyez les sessions et les résultats ; vous changez l'intervalle, le délai, une pause, ou retirez le rôle (ses écoles passent à un autre responsable).", "/admin/formation"),
            new("Votre entraînement", "Le parcours administrateur et celui des responsables, en entraînement libre : sans délai et sans limite d'essais.", "/formation")
        ])
    ];

    private static readonly IReadOnlyList<GuideKapitel> Responsable =
    [
        new("ecoles", "Mes écoles", "school",
        [
            new("Suivre vos écoles", "Statut, nombre d'élèves, inscriptions à valider et contacts de la direction de chaque école.", "/responsable"),
            new("Facturation", "Factures SKOULOU de vos écoles et leur état de paiement (lecture seule).", "/responsable/facturation"),
            new("Mes parts", "Votre part sur chaque facture payée : à recevoir et déjà versée. Une facture annulée après paiement crée un ajustement négatif.", "/responsable/parts"),
            new("Messagerie", "Messages de vos écoles ; vous pouvez répondre et écrire à leur direction.", "/responsable/messagerie")
        ]),
        new("formation", "Formation", "book",
        [
            new("Votre formation", "Dix leçons avec images, vidéos et petits exercices, puis une évaluation à intervalle régulier : 3 essais au plus, seul le dernier compte ; après chaque essai, votre note et vos erreurs.", "/formation")
        ])
    ];

    private static readonly IReadOnlyList<GuideKapitel> Direction =
    [
        new("demarrer", "Démarrer l'année", "check-square",
        [
            new("1. Année scolaire et périodes", "Créez l'année (ex. 2026-2027), choisissez le nombre de périodes (1 période, 2 semestres, 3 trimestres ou sessions), puis démarrez-la.", "/direction/annee"),
            new("2. Classes", "Créez les niveaux, puis les classes de chaque niveau avec leur salle, leur capacité et le professeur principal.", "/direction/classes"),
            new("3. Matières", "Listez les matières avec leur groupe (lettres, sciences…) et leur ordre sur le bulletin, puis réglez leurs coefficients dans chaque classe (« Matières et enseignants »). Une matière peut y être facultative (ex. Ewe, Culture) : choisissez comment elle compte, puis cochez les élèves qui la suivent (« Matières facultatives »). Réglez-la avant les premiers bulletins : une fois publiés, la règle et les choix sont fixés (correction par rectification). Au passage à l'année suivante, les choix sont repris.", "/direction/matieres"),
            new("4. Équipe", "Ajoutez enseignants et coordinateurs et leurs affectations (classe, matière, heures) ; imprimez la liste des accès pour ceux sans e-mail.", "/direction/equipe"),
            new("5. Élèves", "Ajoutez ou importez les élèves (Excel) dans leur classe et validez les inscriptions en ligne. Changements de classe et départs se font aussi ici.", "/direction/eleves"),
            new("6. Parents", "Créez les accès des parents et liez leurs enfants : ils suivent notes, présences, emploi du temps et frais.", "/direction/parents")
        ]),
        new("quotidien", "Au quotidien", "calendar",
        [
            new("Emplois du temps", "Construisez l'emploi du temps de chaque classe ; les conflits d'enseignant, de salle ou de classe sont signalés.", "/direction/emplois-du-temps"),
            new("Présences", "Faites l'appel par classe : tout le monde est présent par défaut, signalez seulement les absents et les retards.", "/presences"),
            new("Notes", "Choisissez la classe, la matière et la période, puis saisissez au clavier : « abs » absent, « aj » absence justifiée, « disp » dispensé. Chaque note est enregistrée aussitôt.", "/notes"),
            new("Bulletins", "Préparez le conseil de classe, puis publiez les bulletins (PDF avec code QR). Une erreur après publication se corrige par un bulletin rectifié.", "/bulletins"),
            new("Contenus", "Toutes les leçons et tous les examens de l'établissement ; saisissez un contenu pour un enseignant qui n'a pas d'appareil.", "/direction/contenus"),
            new("Si vous enseignez", "Vos cours, leçons, examens et corrections se trouvent sous « Mon enseignement ».", "/enseignant")
        ]),
        new("finances", "Finances", "wallet",
        [
            new("Vue d'ensemble", "Recettes, impayés et solde de caisse de l'année.", "/finances"),
            new("Frais de scolarité", "Définissez les frais par niveau ou par classe, avec tranches et échéances. Dans une école de langues, un frais peut concerner une seule session. Les réductions (bourse, fratrie…) s'accordent par élève.", "/finances/frais"),
            new("Paiements", "Encaissez (espèces, Mobile Money, virement, chèque) et imprimez le reçu. Une erreur se corrige par une annulation avec motif.", "/finances/paiements"),
            new("Journal de caisse", "Les vrais mouvements d'argent : ajoutez les dépenses avec leur justificatif. On ne supprime rien : une erreur se corrige par une écriture inverse.", "/finances/journal"),
            new("Facture SKOULOU", "Après chaque période, SKOULOU facture chaque élève inscrit une seule fois. Virez le montant, puis déclarez le paiement ; contestez la facture si un chiffre vous semble faux.", "/finances/facturation"),
            new("Rapport financier", "Recettes et dépenses par catégorie, paiements par moyen et retards des familles sur la période choisie, en PDF.", "/finances/rapport")
        ]),
        new("fin", "Fin d'année", "layers",
        [
            new("Passage d'année", "Selon les décisions de fin d'année : les admis passent au niveau suivant, les redoublants restent au même niveau, les autres quittent l'établissement. Tout se fait en une seule étape, vérifiée avant.", "/direction/annee/passage")
        ]),
        new("administration", "Administration", "school",
        [
            new("Établissement", "Données de l'école, en-tête des documents (bulletins, reçus, cartes), logo et droits des coordinateurs sur les finances.", "/direction/ecole"),
            new("Journal", "Qui a fait quoi, et quand : comptes et mots de passe, inscriptions, notes, bulletins, paiements.", "/direction/journal")
        ])
    ];

    private static readonly IReadOnlyList<GuideKapitel> Enseignant =
    [
        new("cours", "Mes cours", "book-open",
        [
            new("Leçons et exercices", "Créez une leçon avec ses pages et ses questions, puis attribuez-la à une classe.", "/enseignant/lecons"),
            new("Examens", "Préparez un examen chronométré ; les élèves le passent une seule fois, avant la date limite.", "/enseignant/examens"),
            new("Corrections", "Corrigez les réponses libres ; les autres questions sont notées automatiquement.", "/enseignant/corrections")
        ]),
        new("classe", "Ma classe", "users",
        [
            new("Appel", "Faites l'appel de vos classes : tout le monde est présent par défaut, signalez seulement les absents et les retards.", "/presences"),
            new("Notes", "Saisissez les notes dans la grille : « abs » absent, « aj » absence justifiée, « disp » dispensé. Dans une matière facultative, seuls les élèves qui l'ont choisie apparaissent.", "/notes"),
            new("Bulletins", "Professeur principal : préparez le conseil de classe et consultez les bulletins de votre classe.", "/bulletins"),
            new("Emploi du temps", "Votre semaine, classe par classe.", "/enseignant/emploi-du-temps")
        ])
    ];

    private static readonly IReadOnlyList<GuideKapitel> Eleve =
    [
        new("apprendre", "Apprendre", "book-open",
        [
            new("Leçons", "Lisez les leçons de votre classe et faites les exercices ; votre meilleur essai compte.", "/eleve/lecons"),
            new("Examens", "Un examen se passe une seule fois, dans le temps indiqué. Vos réponses sont envoyées automatiquement à la fin.", "/eleve/examens"),
            new("Résultats", "Vos scores aux leçons et aux examens, calculés sur les points obtenus.", "/eleve/resultats"),
            new("Bulletins", "Vos bulletins publiés par l'établissement, à télécharger en PDF.", "/eleve/bulletins")
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
            new("Notes", "Les résultats de votre enfant, selon ce que l'établissement communique : notes au fil de l'eau ou seulement les bulletins.", "/parents/notes"),
            new("Bulletins", "Bulletins publiés par l'établissement, à télécharger en PDF.", "/parents/bulletins"),
            new("Présences", "Absences et retards de votre enfant.", "/parents/presences"),
            new("Emploi du temps", "Les cours de la semaine de votre enfant.", "/parents/emploi-du-temps"),
            new("Frais et reçus", "Échéances, paiements, reste à payer et reçus.", "/parents/frais")
        ])
    ];

    /// <summary>
    /// Kapitel für eine Rolle; « Mon compte » steht immer am Ende. Ohne <paramref name="hatRecht"/> gelten alle
    /// Koordinator-Rechte (Direktor); Schritte zu nicht erreichbaren Seiten und dadurch leere Kapitel entfallen.
    /// </summary>
    public static IReadOnlyList<GuideKapitel> Fuer(string? rolle, Func<KoordinatorRechte, bool>? hatRecht = null, bool hauptAdmin = false)
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
        var ziele = NavKarte.Fuer(rolle ?? string.Empty, hatRecht ?? (_ => true), hauptAdmin).SelectMany(g => g.Eintraege).ToList();
        return [.. eigene.Select(k => k with { Schritte = [.. k.Schritte.Where(s => Erreichbar(s.Href, ziele))] })
                         .Where(k => k.Schritte.Count > 0), Konto];
    }

    /// <summary>
    /// Ziel der Navigation oder eine Unterseite eines Präfix-Ziels (z. B. /direction/annee/passage); Startseiten
    /// (Match All, z. B. /direction) decken keine Unterseiten ab.
    /// </summary>
    private static bool Erreichbar(string? href, IReadOnlyList<NavEintrag> ziele) =>
        href is null || Allgemein.Contains(href) || ziele.Any(z => href == z.Href
            || (z.Match == Microsoft.AspNetCore.Components.Routing.NavLinkMatch.Prefix && href.StartsWith(z.Href + "/", StringComparison.Ordinal)));
}
