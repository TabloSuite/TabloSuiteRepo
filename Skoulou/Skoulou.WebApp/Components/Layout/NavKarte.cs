using Microsoft.AspNetCore.Components.Routing;
using Skoulou.Core.Rechte;

namespace Skoulou.WebApp.Components.Layout;

/// <summary>Ein Navigationsziel (Kurzname für die untere Leiste am Handy).</summary>
public sealed record NavEintrag(string Href, string Label, string Icon, string? Beschreibung = null, string? Kurz = null,
    NavLinkMatch Match = NavLinkMatch.Prefix)
{
    public string KurzLabel => Kurz ?? Label;
}

/// <summary>Gruppe der Seitenleiste (Titel null = ohne Überschrift).</summary>
public sealed record NavGruppe(string? Titel, IReadOnlyList<NavEintrag> Eintraege);

/// <summary>
/// Navigation je Rolle (Bauplan §6): Seitenleiste mit Gruppen, untere Handy-Leiste mit 4 Hauptzielen.
/// Jede Route aller Rollen steht hier; Koordinator sieht Finanzen nur mit Recht, Schule/Journal nur der Direktor.
/// </summary>
public static class NavKarte
{
    private static readonly NavGruppe Hilfe = new("Aide",
    [
        new("/aide/videos", "Vidéos d'aide", "video", "Courtes vidéos pour chaque rôle.", "Vidéos"),
        new("/aide/guide", "Guide utilisateur", "book", "Pas à pas, fonction par fonction.", "Guide"),
        new("/contact", "Contact", "mail", "Écrire à l'équipe SKOULOU."),
    ]);

    /// <param name="hauptAdmin">Hauptadministrator: zusätzlich « Administrateurs » (nur er verwaltet die anderen Administratoren).</param>
    public static IReadOnlyList<NavGruppe> Fuer(string rolle, Func<KoordinatorRechte, bool> hatRecht, bool hauptAdmin = false) => rolle switch
    {
        Rollen.SuperAdmin => hauptAdmin ? AdminMitVerwaltung : Admin,
        Rollen.Responsable => Responsable,
        Rollen.Direktor or Rollen.Koordinator => Direktion(rolle == Rollen.Direktor, hatRecht),
        Rollen.Lehrer => Lehrer,
        Rollen.Schueler => Schueler,
        Rollen.Eltern => Eltern,
        _ => [Hilfe]
    };

    /// <summary>Hauptziele der unteren Handy-Leiste (der Rest liegt im Blatt « Plus »).</summary>
    public static IReadOnlyList<NavEintrag> Mobil(string rolle, Func<KoordinatorRechte, bool> hatRecht)
    {
        var alle = Fuer(rolle, hatRecht).SelectMany(g => g.Eintraege).ToList();
        string[] wahl = rolle switch
        {
            Rollen.SuperAdmin => ["/admin", "/admin/ecoles", "/admin/comptes", "/admin/messagerie"],
            Rollen.Responsable => ["/responsable", "/responsable/facturation", "/responsable/parts", "/responsable/messagerie"],
            Rollen.Direktor or Rollen.Koordinator => ["/direction", "/direction/eleves", "/direction/classes", "/notes"],
            Rollen.Lehrer => ["/enseignant", "/notes", "/presences", "/enseignant/lecons"],
            Rollen.Schueler => ["/eleve", "/eleve/lecons", "/eleve/examens", "/eleve/resultats"],
            Rollen.Eltern => ["/parents", "/parents/notes", "/parents/bulletins", "/parents/frais"],
            _ => []
        };
        return wahl.Select(h => alle.FirstOrDefault(e => e.Href == h)).OfType<NavEintrag>().ToList();
    }

    private static readonly IReadOnlyList<NavGruppe> Admin = AdminGruppen(false);
    private static readonly IReadOnlyList<NavGruppe> AdminMitVerwaltung = AdminGruppen(true);

    private static IReadOnlyList<NavGruppe> AdminGruppen(bool mitVerwaltung)
    {
        var plattform = new List<NavEintrag>
        {
            new("/admin/ecoles", "Écoles", "school", "Valider, suspendre, tarif et responsable."),
            new("/admin/comptes", "Comptes", "users", "Tous les comptes de la plateforme."),
            new("/admin/responsables", "Responsables", "briefcase", "Responsables SKOULOU et leurs écoles."),
        };
        if (mitVerwaltung)
            plattform.Add(new("/admin/administrateurs", "Administrateurs", "shield", "Ajouter, bloquer ou supprimer les autres administrateurs.", "Admins"));
        return
        [
            new(null, [new("/admin", "Tableau de bord", "home", Kurz: "Accueil", Match: NavLinkMatch.All)]),
            new("Plateforme", plattform),
            new("Facturation",
            [
                new("/admin/facturation", "Facturation", "receipt", "Factures SKOULOU de toutes les écoles."),
                new("/admin/tarifs", "Tarifs", "tag", "Prix annuel par élève et délai de paiement."),
            ]),
            new("Communication",
            [
                new("/admin/annonces", "Annonces", "megaphone", "Messages dans l'application."),
                new("/admin/messagerie", "Messagerie", "inbox", "E-mails reçus et envoyés.", "Messages"),
            ]),
            new("Surveillance",
            [
                new("/admin/journal", "Journal des erreurs", "alert", "Erreurs techniques récentes.", "Erreurs"),
                new("/admin/audit", "Audit", "shield", "Actions sensibles de toutes les écoles."),
                new("/admin/medias", "Médias", "image", "Fichiers stockés par école."),
            ]),
            Hilfe,
        ];
    }

    private static readonly IReadOnlyList<NavGruppe> Responsable =
    [
        new(null,
        [
            new("/responsable", "Mes écoles", "school", "Les établissements que vous accompagnez.", "Écoles", NavLinkMatch.All),
            new("/responsable/facturation", "Facturation", "receipt", "Factures SKOULOU de vos écoles."),
            new("/responsable/parts", "Mes parts", "coins", "Vos parts sur les factures payées.", "Parts"),
            new("/responsable/messagerie", "Messagerie", "inbox", "E-mails de vos écoles.", "Messages"),
        ]),
        Hilfe,
    ];

    private static IReadOnlyList<NavGruppe> Direktion(bool istDirektor, Func<KoordinatorRechte, bool> hatRecht)
    {
        var gruppen = new List<NavGruppe>
        {
            new(null, [new("/direction", "Accueil", "home", Match: NavLinkMatch.All)]),
            new("Établissement",
            [
                new("/direction/eleves", "Élèves", "graduation", "Inscriptions, classes et comptes des élèves."),
                new("/direction/classes", "Classes", "layers", "Classes, niveaux et professeurs principaux."),
                new("/direction/equipe", "Équipe", "users", "Enseignants, coordination et affectations."),
                new("/direction/matieres", "Matières", "book", "Matières et coefficients."),
                new("/direction/annee", "Année scolaire", "calendar", "Périodes, composantes et passage d'année.", "Année"),
                new("/direction/emplois-du-temps", "Emplois du temps", "timetable", "Emplois du temps des classes.", "Horaires"),
                new("/direction/parents", "Parents", "famille", "Comptes des parents et enfants liés."),
            ]),
            new("Pédagogie",
            [
                new("/notes", "Notes", "clipboard", "Saisie et suivi des notes."),
                new("/presences", "Présences", "user-check", "Appel et absences."),
                new("/bulletins", "Bulletins", "report", "Conseil de classe et publication."),
                new("/direction/contenus", "Contenus", "book-open", "Leçons et examens des enseignants."),
            ]),
            new("Mon enseignement",
            [
                new("/enseignant", "Mes cours", "pen", "Vos classes et vos cours.", Match: NavLinkMatch.All),
                new("/enseignant/lecons", "Leçons", "book-open", "Vos leçons et exercices."),
                new("/enseignant/examens", "Examens", "check-square", "Vos examens en ligne."),
                new("/enseignant/corrections", "Corrections", "clipboard", "Réponses à corriger."),
                new("/enseignant/emploi-du-temps", "Mon emploi du temps", "timetable", "Vos heures de cours.", "Horaires"),
            ]),
        };

        var finanzen = new List<NavEintrag>();
        var irgendeinRecht = hatRecht(KoordinatorRechte.FinanzenEinsehen) || hatRecht(KoordinatorRechte.ZahlungenErfassen)
                             || hatRecht(KoordinatorRechte.BuchhaltungErfassen) || hatRecht(KoordinatorRechte.RechnungSkoulou);
        if (irgendeinRecht) finanzen.Add(new("/finances", "Vue d'ensemble", "wallet", "Recettes, impayés et solde de caisse.", "Finances", NavLinkMatch.All));
        if (hatRecht(KoordinatorRechte.ZahlungenErfassen))
        {
            finanzen.Add(new("/finances/paiements", "Paiements", "banknote", "Encaisser et imprimer les reçus."));
            finanzen.Add(new("/finances/frais", "Frais de scolarité", "tag", "Frais, tranches et réductions.", "Frais"));
        }
        if (hatRecht(KoordinatorRechte.BuchhaltungErfassen)) finanzen.Add(new("/finances/journal", "Journal de caisse", "log", "Recettes et dépenses.", "Caisse"));
        if (hatRecht(KoordinatorRechte.RechnungSkoulou)) finanzen.Add(new("/finances/facturation", "Facturation SKOULOU", "receipt", "Factures de la plateforme.", "Factures"));
        if (hatRecht(KoordinatorRechte.FinanzenEinsehen)) finanzen.Add(new("/finances/rapport", "Rapport financier", "bars", "Rapport par période.", "Rapport"));
        if (finanzen.Count > 0) gruppen.Add(new("Finances", finanzen));

        if (istDirektor)
        {
            gruppen.Add(new("Administration",
            [
                new("/direction/ecole", "Établissement", "school", "Données de l'école, logo, droits des coordinateurs.", "École"),
                new("/direction/journal", "Journal", "history", "Actions sensibles de l'établissement."),
            ]));
        }
        gruppen.Add(Hilfe);
        return gruppen;
    }

    private static readonly IReadOnlyList<NavGruppe> Lehrer =
    [
        new(null, [new("/enseignant", "Accueil", "home", Match: NavLinkMatch.All)]),
        new("Ma classe",
        [
            new("/notes", "Notes", "clipboard", "Saisir les notes de vos classes."),
            new("/presences", "Présences", "user-check", "Faire l'appel."),
            new("/bulletins", "Bulletins", "report", "Bulletins de votre classe (professeur principal)."),
            new("/enseignant/emploi-du-temps", "Emploi du temps", "timetable", "Vos heures de cours.", "Horaires"),
        ]),
        new("Contenus",
        [
            new("/enseignant/lecons", "Leçons", "book-open", "Leçons et exercices."),
            new("/enseignant/examens", "Examens", "check-square", "Examens en ligne."),
            new("/enseignant/corrections", "Corrections", "pen", "Réponses à corriger."),
        ]),
        Hilfe,
    ];

    private static readonly IReadOnlyList<NavGruppe> Schueler =
    [
        new(null,
        [
            new("/eleve", "Accueil", "home", Match: NavLinkMatch.All),
            new("/eleve/lecons", "Leçons", "book-open", "Leçons et exercices de votre classe."),
            new("/eleve/examens", "Examens", "check-square", "Vos examens en ligne."),
            new("/eleve/resultats", "Résultats", "trend-up", "Vos notes et votre progression."),
            new("/eleve/bulletins", "Bulletins", "report", "Vos bulletins publiés."),
            new("/eleve/emploi-du-temps", "Emploi du temps", "timetable", "Vos cours de la semaine.", "Horaires"),
            new("/eleve/frais", "Frais de scolarité", "wallet", "Ce qui est payé et ce qui reste.", "Frais"),
        ]),
        Hilfe,
    ];

    private static readonly IReadOnlyList<NavGruppe> Eltern =
    [
        new(null,
        [
            new("/parents", "Mes enfants", "famille", "Les enfants liés à votre compte.", "Enfants", NavLinkMatch.All),
            new("/parents/notes", "Notes", "clipboard", "Notes de vos enfants."),
            new("/parents/bulletins", "Bulletins", "report", "Bulletins publiés."),
            new("/parents/presences", "Présences", "user-check", "Absences et retards."),
            new("/parents/emploi-du-temps", "Emploi du temps", "timetable", "Cours de la semaine.", "Horaires"),
            new("/parents/frais", "Frais et reçus", "wallet", "Frais de scolarité et reçus.", "Frais"),
        ]),
        Hilfe,
    ];
}
