using System.Security.Claims;
using ENote.Core.DataModels.Identity;

namespace ENote.WebApp.Services;

/// <summary>
/// Erklärvideos je Rolle. Sichtbarkeit: Admin und ProduktOwner sehen alle, der Besitzer seine und die
/// des Kassierers, der Kassierer nur seine. Das Werbevideo ist öffentlich (Startseite).
/// Dateien liegen in <c>Medien/videos</c>, Vorschaubilder in <c>wwwroot/videos</c>.
/// </summary>
public static class VideoKatalog
{
    public sealed record LernVideo(string Datei, string Titel, string Beschreibung, string Fuer, IReadOnlyList<string> Rollen)
    {
        public string Url => "/medien/videos/" + Datei;
        public string Poster => "videos/" + Path.ChangeExtension(Datei, ".jpg");
        public bool DarfSehen(ClaimsPrincipal user) => Rollen.Any(user.IsInRole);
    }

    public const string Werbung = "enote-presentation.mp4";

    private static readonly string[] Alle = [AppRollen.SuperAdmin, AppRollen.ProduktOwner, AppRollen.Besitzer, AppRollen.Kassierer];
    private static readonly string[] AbBesitzer = [AppRollen.SuperAdmin, AppRollen.ProduktOwner, AppRollen.Besitzer];
    private static readonly string[] Plattform = [AppRollen.SuperAdmin, AppRollen.ProduktOwner];

    public static readonly IReadOnlyList<LernVideo> Videos =
    [
        new("enote-caissier.mp4", "Encaisser avec eNOTE",
            "Ouvrir la caisse, encaisser en espèces ou Mobile Money, imprimer le reçu et clôturer le soir.", "Employé", Alle),
        new("enote-proprietaire.mp4", "Gérer votre établissement",
            "Prestations et prix, équipe et employés, finances, dépenses et abonnement.", "Propriétaire", AbBesitzer),
        new("enote-responsable.mp4", "Suivre vos établissements",
            "Vos établissements, les paiements d'abonnement et vos commissions.", "Responsable", Plattform),
        new("enote-admin.mp4", "Piloter la plateforme",
            "Valider les inscriptions, tarifs, facturation, annonces et e-mails.", "Administrateur", Plattform),
    ];

    /// <summary>
    /// Adresse mit Versionsnummer aus dem Dateidatum („?v=…“): nach einem Deploy lädt der Browser
    /// sofort die neue Fassung statt der zwischengespeicherten.
    /// </summary>
    public static string MitVersion(string url, string? pfad)
        => pfad is not null && File.Exists(pfad) ? $"{url}?v={File.GetLastWriteTimeUtc(pfad).Ticks:x}" : url;

    public static LernVideo? Finde(string datei) => Videos.FirstOrDefault(v => string.Equals(v.Datei, datei, StringComparison.OrdinalIgnoreCase));

    /// <summary>Darf dieser Nutzer die Datei abrufen? (Werbevideo immer, Erklärvideos nach Rolle.)</summary>
    public static bool DarfAbrufen(string datei, ClaimsPrincipal user)
        => string.Equals(datei, Werbung, StringComparison.OrdinalIgnoreCase)
           || (user.Identity?.IsAuthenticated == true && Finde(datei)?.DarfSehen(user) == true);
}
