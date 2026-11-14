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
        // Premiers pas + alle Employé-Videos (Paket A), Propriétaire (Paket B) – die übrigen folgen paketweise)
        new("enote-premiers-pas.mp4", "Premiers pas : se repérer",
            "Se connecter, l'accueil, le menu, les boutons et les fenêtres, votre compte, la zone du propriétaire.", Alle_, Alle, "4:14"),
        new("enote-visite-guidee.mp4", "La visite guidée",
            "Toutes les fonctions d'eNOTE, où les trouver et comment s'en servir au quotidien.", Alle_, Alle, "5:10"),
        new(Werbung, "Pourquoi eNOTE ?",
            "Ce que eNOTE change au quotidien pour un salon, une boutique ou un petit cabinet.", Alle_, Alle, "2:54"),

        new("enote-employe-encaisser.mp4", "Ouvrir la caisse et encaisser",
            "Ouvrir la caisse, l'écran de caisse, remplir le ticket, client et remise, encaisser en espèces, Mobile Money ou carte.", "Employé", Alle, "5:24"),
        new("enote-employe-attente.mp4", "Mettre un ticket en attente",
            "Garder un ticket pour plus tard, le reprendre ou le supprimer ; pour un client présent : « Enregistrer sans encaisser ».", "Employé", Alle, "3:07"),
        new("enote-employe-clients.mp4", "Accueillir les clients",
            "Noter une arrivée, créer un client, la carte et la fiche, enregistrer sans encaisser, encaisser au départ ou noter le départ.", "Employé", Alle, "4:38"),
        new("enote-employe-cloture.mp4", "Rembourser et clôturer la caisse",
            "Retrouver une vente, réimprimer, rembourser, retrait du propriétaire, clôture du soir et écart de caisse.", "Employé", Alle, "3:43"),

        new("enote-proprietaire-catalogue.mp4", "Préparer votre catalogue",
            "Les catégories, une prestation, un produit avec prix d'achat, stock et code-barres, étiquettes et pertes, les remises.", "Propriétaire", AbBesitzer, "3:41"),
        new("enote-proprietaire-equipe.mp4", "Votre équipe et vos réglages",
            "La page Équipe, ajouter un employé avec son code PIN, ses droits, votre reçu, Mobile Money, salles et postes, votre code PIN.", "Propriétaire", AbBesitzer, "3:42"),
        new("enote-proprietaire-activite.mp4", "Suivre votre activité",
            "Les chiffres du jour sur l'accueil, les finances par période (solde, entrées, dépenses, clôtures), noter une dépense, apport et rapport PDF.", "Propriétaire", AbBesitzer, "3:12"),
        new("enote-proprietaire-clients.mp4", "Vos clients",
            "Le fichier clients, la fiche avec achats, préférés, note privée et notes de l'équipe, un nouveau client, une remise pour les fidèles.", "Propriétaire", AbBesitzer, "2:36"),
        new("enote-proprietaire-compte.mp4", "Abonnement et compte",
            "Essai gratuit et formule, factures et « J'ai payé », résiliation et suppression, votre profil et votre mot de passe.", "Propriétaire", AbBesitzer, "2:41"),

        new("enote-responsable-etablissements.mp4", "Vos établissements",
            "Suivre vos établissements, leurs ventes et votre commission, inscrire un nouveau commerçant.", "Responsable", Plattform, "1:28"),
        new("enote-responsable-paiements.mp4", "Paiements et accompagnement",
            "Abonnements à payer et paiements signalés, votre profil, les vidéos d'aide pour vos commerçants.", "Responsable", Plattform, "1:09"),

        new("enote-admin-etablissements.mp4", "Gérer les établissements",
            "Vue d'ensemble, période d'essai, tarif personnalisé, nouveau propriétaire avec son établissement.", "Administrateur", Plattform, "1:38"),
        new("enote-admin-tarifs.mp4", "Tarifs et facturation",
            "Tarif général et historique, responsables et commissions, Mobile Money, confirmation des paiements.", "Administrateur", Plattform, "1:26"),
        new("enote-admin-communication.mp4", "Communiquer",
            "E-mails aux propriétaires et responsables, envois automatiques, modèles, annonces, journaux d'erreurs.", "Administrateur", NurAdmin, "1:30"),
    ];

    private static readonly (string Fuer, string Titel, string Untertitel)[] GruppenInfo =
    [
        (Alle_, "Pour bien commencer", "Les premiers pas, la visite guidée et pourquoi eNOTE."),
        ("Employé", "Employé", "Encaisser, mettre un ticket en attente, accueillir les clients et clôturer la caisse."),
        ("Propriétaire", "Propriétaire", "Préparer le catalogue et l'équipe, suivre l'activité, fidéliser les clients."),
        ("Responsable", "Responsable", "Accompagner vos établissements."),
        ("Administrateur", "Administrateur", "Piloter la plateforme."),
    ];

    /// <summary>
    /// Sichtbare Videos gruppiert: « Pour bien commencer » zuerst, dann die eigene Rolle, danach die übrigen.
    /// </summary>
    public static IReadOnlyList<Gruppe> FuerNutzer(ClaimsPrincipal user)
    {
        var eigene = user.IsInRole(AppRollen.SuperAdmin) ? "Administrateur"
            : user.IsInRole(AppRollen.ProduktOwner) ? "Responsable"
            : user.IsInRole(AppRollen.Besitzer) ? "Propriétaire"
            : "Employé";
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
