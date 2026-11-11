using System.Security.Claims;
using ENote.Core.DataModels.Identity;

namespace ENote.WebApp.Services;

/// <summary>
/// Lernvideos: mehrere kurze Videos je Rolle (Anwendungsfälle) plus « Pourquoi eNOTE ? » für alle.
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
        new("enote-pourquoi.mp4", "Pourquoi eNOTE ?",
            "Ce que eNOTE change au quotidien pour un salon, une boutique ou un petit cabinet.", Alle_, Alle, "1:28"),

        new("enote-employe-encaisser.mp4", "Encaisser une vente",
            "Ouvrir la caisse, composer le ticket, ajouter le client, encaisser en espèces ou Mobile Money.", "Employé", Alle, "1:22"),
        new("enote-employe-clients.mp4", "Accueillir les clients",
            "Noter l'arrivée, le motif et la salle, consulter la fiche et ajouter des notes.", "Employé", Alle, "1:10"),
        new("enote-employe-cloture.mp4", "Rembourser et clôturer",
            "Retrouver une vente, rembourser, écran client, retrait et clôture du soir.", "Employé", Alle, "0:53"),

        new("enote-proprietaire-demarrer.mp4", "Bien démarrer",
            "Zone propriétaire, prestations et catégories, stock, équipe et droits, reçu et Mobile Money.", "Propriétaire", AbBesitzer, "1:06"),
        new("enote-proprietaire-activite.mp4", "Suivre votre activité",
            "Accueil, finances par période, clôtures de caisse, dépenses avec justificatif, inventaire.", "Propriétaire", AbBesitzer, "0:54"),
        new("enote-proprietaire-clients.mp4", "Fidéliser vos clients",
            "Fiches clients, passages et notes de l'équipe, note privée, clients présents et remises.", "Propriétaire", AbBesitzer, "0:53"),
        new("enote-proprietaire-compte.mp4", "Abonnement et compte",
            "Formule, factures, résiliation ou suppression du compte, profil personnel.", "Propriétaire", AbBesitzer, "0:50"),

        new("enote-responsable-etablissements.mp4", "Vos établissements",
            "Suivre ventes et commission de chaque établissement, inscrire un nouveau commerçant.", "Responsable", Plattform, "0:43"),
        new("enote-responsable-paiements.mp4", "Paiements et accompagnement",
            "Abonnements à payer, paiements signalés, profil et vidéos à montrer aux commerçants.", "Responsable", Plattform, "0:37"),

        new("enote-admin-etablissements.mp4", "Gérer les établissements",
            "Valider les inscriptions, période d'essai, tarif personnalisé, nouveau propriétaire.", "Administrateur", Plattform, "0:48"),
        new("enote-admin-tarifs.mp4", "Tarifs et facturation",
            "Tarif général, responsables et commissions, Mobile Money, confirmation des paiements.", "Administrateur", Plattform, "0:42"),
        new("enote-admin-communication.mp4", "Communiquer",
            "E-mails aux propriétaires et responsables, envois automatiques, modèles, annonces, journaux.", "Administrateur", NurAdmin, "0:46"),
    ];

    private static readonly (string Fuer, string Titel, string Untertitel)[] GruppenInfo =
    [
        (Alle_, "Pourquoi eNOTE ?", "En une minute, ce que eNOTE change au quotidien."),
        ("Employé", "Employé", "Encaisser, accueillir les clients et clôturer la caisse."),
        ("Propriétaire", "Propriétaire", "Préparer l'établissement, suivre l'activité, fidéliser les clients."),
        ("Responsable", "Responsable", "Accompagner vos établissements."),
        ("Administrateur", "Administrateur", "Piloter la plateforme."),
    ];

    /// <summary>
    /// Sichtbare Videos gruppiert: « Pourquoi eNOTE ? » zuerst, dann die eigene Rolle, danach die übrigen.
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
