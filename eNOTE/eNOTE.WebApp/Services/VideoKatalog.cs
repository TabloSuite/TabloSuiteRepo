using System.Security.Claims;
using ENote.Core.DataModels.Identity;

namespace ENote.WebApp.Services;

/// <summary>
/// Lernvideos: mehrere Videos je Rolle (Anwendungsfälle) plus « Pour bien commencer » für alle
/// (Premiers pas, Visite guidée, « Pourquoi eNOTE ? » = Werbevideo der Startseite).
/// Sichtbarkeit: Admin sieht alle, Responsable alles außer der Admin-Kommunikation, der Besitzer seine
/// und die der Employés, der Employé nur seine. Das Werbevideo ist öffentlich (Startseite).
/// Dateien liegen in <c>Medien/videos</c>, Vorschaubilder in <c>wwwroot/videos</c>.
/// </summary>
public static class VideoKatalog
{
    public sealed record LernVideo(string Datei, string Titel, string Beschreibung, string Fuer, IReadOnlyList<string> Rollen, string Dauer)
    {
        public string Url => "/medien/videos/" + Datei;
        public string Poster => "videos/" + Path.ChangeExtension(Datei, ".jpg");
        public bool DarfSehen(ClaimsPrincipal user) => Rollen.Any(user.IsInRole);
    }

    /// <summary>Eine Gruppe auf der Seite « Vidéos d'aide » (je Rolle, in Lernreihenfolge).</summary>
    public sealed record Gruppe(string Fuer, string Titel, string Untertitel, IReadOnlyList<LernVideo> Videos);

    public const string Werbung = "enote-presentation.mp4";
    public const string Alle_ = "Pour tous";

    private static readonly string[] Alle = [AppRollen.SuperAdmin, AppRollen.ProduktOwner, AppRollen.Besitzer, AppRollen.Kassierer];
    private static readonly string[] AbBesitzer = [AppRollen.SuperAdmin, AppRollen.ProduktOwner, AppRollen.Besitzer];
    private static readonly string[] Plattform = [AppRollen.SuperAdmin, AppRollen.ProduktOwner];
    private static readonly string[] NurAdmin = [AppRollen.SuperAdmin];

    public static readonly IReadOnlyList<LernVideo> Videos =
    [
        // Für alle (12.11.2026: Lernvideos mit Sprecherin; ab 14.11.2026 nach und nach im v3-Stil ohne Sprecherin:
        // Premiers pas + alle Employé-Videos (Paket A), Propriétaire (Paket B), Responsable + Administrateur (Paket C),
        // Visite guidée + Werbevideo (Paket D))
        new("enote-premiers-pas.mp4", "Premiers pas : se repérer",
            "Se connecter, l'accueil, le menu, les boutons et les fenêtres, votre compte, la zone du propriétaire.", Alle_, Alle, "4:14"),
        new("enote-visite-guidee.mp4", "La visite guidée",
            "Qui voit quoi et où se trouve chaque fonction : accueil, caisse, clients présents, zone du propriétaire, responsable et administrateur.", Alle_, Alle, "5:32"),
        new(Werbung, "Pourquoi eNOTE ?",
            "En cinq minutes : le cahier du matin, puis la preuve dans l'application – catalogue et catégories, vente rapide, clients, clôture sans écart, finances et équipe. Pour une boutique, un salon ou un petit cabinet.", Alle_, Alle, "5:34"),

        // 16.11.2026: Konzeptvideos (Folien, Theorie vor der Praxis) jeweils vor dem passenden Praxisvideo
        new("enote-employe-concept-caisse.mp4", "Le concept : la caisse, de l'ouverture à la clôture",
            "La théorie avant la pratique : une journée de caisse, ouvrir avec son code PIN, vendre et encaisser, le ticket en attente, le remboursement, la clôture et l'écart, qui fait quoi.", "Employé", Alle, "4:59"),
        new("enote-employe-encaisser.mp4", "Ouvrir la caisse et encaisser",
            "Ouvrir la caisse, l'écran de caisse, remplir le ticket, client et remise, encaisser en espèces, Mobile Money ou carte.", "Employé", Alle, "5:24"),
        new("enote-employe-attente.mp4", "Mettre un ticket en attente",
            "Garder un ticket pour plus tard, le reprendre ou le supprimer ; pour un client présent : « Enregistrer sans encaisser ».", "Employé", Alle, "3:07"),
        new("enote-employe-clients.mp4", "Accueillir les clients",
            "Noter une arrivée, créer un client, la carte et la fiche, enregistrer sans encaisser, encaisser au départ ou noter le départ.", "Employé", Alle, "4:38"),
        new("enote-employe-cloture.mp4", "Rembourser et clôturer la caisse",
            "Retrouver une vente, réimprimer, rembourser, retrait du propriétaire, clôture du soir et écart de caisse.", "Employé", Alle, "3:43"),

        new("enote-proprietaire-concept-catalogue.mp4", "Le concept : catalogue, stock et prix",
            "La théorie avant la pratique : prestation ou produit, les catégories, le prix et la marge, les remises, le stock qui se tient tout seul, les alertes et l'inventaire, qui peut changer quoi.", "Propriétaire", AbBesitzer, "4:40"),
        new("enote-proprietaire-catalogue.mp4", "Préparer votre catalogue",
            "Les catégories, une prestation, un produit avec prix d'achat, stock et code-barres, étiquettes et pertes, les remises.", "Propriétaire", AbBesitzer, "3:41"),
        new("enote-proprietaire-concept-equipe.mp4", "Le concept : votre équipe, les droits et les codes PIN",
            "La théorie avant la pratique : qui fait quoi, les trois clés (mot de passe, code PIN de caisse, code PIN du propriétaire), un nouvel employé, les droits, la zone du propriétaire, la relève à la caisse.", "Propriétaire", AbBesitzer, "3:44"),
        new("enote-proprietaire-equipe.mp4", "Votre équipe et vos réglages",
            "La page Équipe, ajouter un employé avec son code PIN, ses droits, votre reçu, Mobile Money, salles et postes, votre code PIN.", "Propriétaire", AbBesitzer, "3:42"),
        new("enote-proprietaire-concept-chiffres.mp4", "Le concept : vos chiffres",
            "La théorie avant la pratique : chiffre d'affaires, panier moyen et écart ; ce qui entre, ce qui sort, ce qui reste ; le solde n'est pas le bénéfice ; la clôture du soir ; où regarder ; qui voit quoi.", "Propriétaire", AbBesitzer, "4:18"),
        new("enote-proprietaire-activite.mp4", "Suivre votre activité",
            "Les chiffres du jour sur l'accueil, les finances par période (solde, entrées, dépenses, clôtures), noter une dépense, apport et rapport PDF.", "Propriétaire", AbBesitzer, "3:12"),
        new("enote-proprietaire-concept-clients.mp4", "Le concept : vos clients et leur fidélité",
            "La théorie avant la pratique : la fiche client et l'accord pour le téléphone, le client à la caisse, ce que vous apprenez, les clients présents, une remise pour les fidèles, le respect des données.", "Propriétaire", AbBesitzer, "3:23"),
        new("enote-proprietaire-clients.mp4", "Vos clients",
            "Le fichier clients, la fiche avec achats, préférés, note privée et notes de l'équipe, un nouveau client, une remise pour les fidèles.", "Propriétaire", AbBesitzer, "2:36"),
        new("enote-proprietaire-compte.mp4", "Abonnement et compte",
            "Essai gratuit et formule, factures et « J'ai payé », résiliation et suppression, votre profil et votre mot de passe.", "Propriétaire", AbBesitzer, "2:41"),

        new("enote-responsable-etablissements.mp4", "Vos établissements",
            "L'accueil, vos établissements avec leurs ventes et votre commission, inscrire un commerçant, valider une inscription, l'essai et les demandes à l'administrateur.", "Responsable", Plattform, "3:41"),
        new("enote-responsable-paiements.mp4", "Paiements et accompagnement",
            "La facturation et les paiements signalés, votre profil avec votre téléphone, et la page « Aide & contact » de vos commerçants.", "Responsable", Plattform, "2:46"),

        new("enote-admin-etablissements.mp4", "Gérer les établissements",
            "Vue d'ensemble et recherche, valider une inscription, période d'essai, tarif personnalisé, désactiver, nouveau propriétaire avec son établissement.", "Administrateur", Plattform, "3:31"),
        new("enote-admin-tarifs.mp4", "Tarifs et facturation",
            "Tarif général et historique, changer le tarif avec message aux propriétaires, responsables et commissions, Mobile Money, confirmer les paiements.", "Administrateur", Plattform, "3:49"),
        new("enote-admin-communication.mp4", "Communiquer",
            "E-mails aux propriétaires et responsables, envois automatiques et modèles, annonces dans l'application, journaux d'erreurs.", "Administrateur", NurAdmin, "3:24"),
    ];

    private static readonly (string Fuer, string Titel, string Untertitel)[] GruppenInfo =
    [
        (Alle_, "Pour bien commencer", "Les premiers pas, la visite guidée et pourquoi eNOTE."),
        ("Employé", "Employé", "Encaisser, mettre un ticket en attente, accueillir les clients et clôturer la caisse."),
        ("Propriétaire", "Propriétaire", "Préparer le catalogue et l'équipe, suivre l'activité, fidéliser les clients."),
        ("Responsable", "Responsable", "Accompagner vos établissements."),
        ("Administrateur", "Administrateur", "Piloter la plateforme."),
    ];

    /// <summary>Gruppe der eigenen Rolle (erscheint nach « Pour bien commencer », auf der Seite als « Votre rôle » markiert).</summary>
    public static string EigeneGruppe(ClaimsPrincipal user)
        => user.IsInRole(AppRollen.SuperAdmin) ? "Administrateur"
            : user.IsInRole(AppRollen.ProduktOwner) ? "Responsable"
            : user.IsInRole(AppRollen.Besitzer) ? "Propriétaire"
            : "Employé";

    /// <summary>
    /// Sichtbare Videos gruppiert: « Pour bien commencer » zuerst, dann die eigene Rolle, danach die übrigen.
    /// </summary>
    public static IReadOnlyList<Gruppe> FuerNutzer(ClaimsPrincipal user)
    {
        var eigene = EigeneGruppe(user);
        return GruppenInfo
            .Select(g => new Gruppe(g.Fuer, g.Titel, g.Untertitel, Videos.Where(v => v.Fuer == g.Fuer && v.DarfSehen(user)).ToList()))
            .Where(g => g.Videos.Count > 0)
            .OrderBy(g => g.Fuer == Alle_ ? 0 : g.Fuer == eigene ? 1 : 2)
            .ToList();
    }

    /// <summary>
    /// Adresse mit Versionsnummer aus dem Dateidatum („?v=…“): nach einem Deploy lädt der Browser
    /// sofort die neue Fassung statt der zwischengespeicherten.
    /// </summary>
    public static string MitVersion(string url, string? pfad)
        => pfad is not null && File.Exists(pfad) ? $"{url}?v={File.GetLastWriteTimeUtc(pfad).Ticks:x}" : url;

    public static LernVideo? Finde(string datei) => Videos.FirstOrDefault(v => string.Equals(v.Datei, datei, StringComparison.OrdinalIgnoreCase));

    /// <summary>Darf dieser Nutzer die Datei abrufen? (Werbevideo immer, Lernvideos nach Rolle.)</summary>
    public static bool DarfAbrufen(string datei, ClaimsPrincipal user)
        => string.Equals(datei, Werbung, StringComparison.OrdinalIgnoreCase)
           || (user.Identity?.IsAuthenticated == true && Finde(datei)?.DarfSehen(user) == true);
}
