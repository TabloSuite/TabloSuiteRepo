using System.Security.Claims;
using SkoulouApp.Core.DataModels.Identity;

namespace SkoulouApp.WebApp.Services;

/// <summary>
/// Lernvideos: kurze Videos je Rolle (Direction, Enseignant, Élève, Parent) plus « Pourquoi SKOULOU ? » für alle.
/// Sichtbarkeit: die Leitung (Direktion, Koordination, Plattform) sieht alle, damit sie Lehrkräfte, Schüler und
/// Eltern begleiten kann; Lehrkräfte sehen ihre und die der Schüler; Schüler und Eltern nur ihre eigenen.
/// Das Werbevideo ist öffentlich (Startseite). Dateien liegen in <c>Medien/videos</c>, Vorschaubilder in <c>wwwroot/videos</c>.
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

    public const string Werbung = "skoulou-presentation.mp4";
    public const string PourTous = "Pour tous";
    public const string Direction = "Direction";
    public const string Enseignant = "Enseignant";
    public const string Eleve = "Élève";
    public const string Parent = "Parent";

    private static readonly string[] Leitung = [Rollen.SuperAdmin, Rollen.ProduktOwner, Rollen.SchulLeiter, Rollen.Koordinator];
    private static readonly string[] Alle = [.. Leitung, Rollen.Lehrer, Rollen.Schueler, Rollen.Eltern];
    private static readonly string[] FuerLehrkraefte = [.. Leitung, Rollen.Lehrer];
    private static readonly string[] FuerSchueler = [.. Leitung, Rollen.Lehrer, Rollen.Schueler];
    private static readonly string[] FuerEltern = [.. Leitung, Rollen.Eltern];

    public static readonly IReadOnlyList<LernVideo> Videos =
    [
        new("skoulou-pourquoi.mp4", "Pourquoi SKOULOU ?",
            "Direction, enseignants, élèves et parents : ce que SKOULOU change au quotidien.", PourTous, Alle, "0:47"),

        new("skoulou-direction-demarrer.mp4", "Préparer l'établissement",
            "Créer l'année scolaire et les classes, inscrire les élèves, ajouter les enseignants, publier l'emploi du temps.", Direction, Leitung, "1:03"),
        new("skoulou-direction-suivre.mp4", "Notes, bulletins et paiements",
            "Contrôler les notes, prévisualiser et publier les bulletins, suivre les paiements, ouvrir l'accès aux parents.", Direction, Leitung, "0:51"),

        new("skoulou-enseignant-cours.mp4", "Créer leçons et examens",
            "Rédiger une leçon, ajouter des exercices et une vidéo, publier, créer un examen corrigé automatiquement.", Enseignant, FuerLehrkraefte, "0:52"),
        new("skoulou-enseignant-notes.mp4", "Suivre la classe et noter",
            "Suivre la progression de chaque élève, consulter l'emploi du temps, saisir les notes et voir les moyennes.", Enseignant, FuerLehrkraefte, "0:37"),

        new("skoulou-eleve-apprendre.mp4", "Apprendre avec SKOULOU",
            "Suivre sa progression, ouvrir une leçon, faire les exercices, passer un examen et revoir ses résultats.", Eleve, FuerSchueler, "0:48"),

        new("skoulou-parent-suivre.mp4", "Suivre votre enfant",
            "Consulter les bulletins publiés, les absences et retards, les frais de scolarité et les reçus.", Parent, FuerEltern, "0:29"),
    ];

    private static readonly (string Fuer, string Titel, string Untertitel)[] GruppenInfo =
    [
        (PourTous, "Pourquoi SKOULOU ?", "En moins d'une minute, ce que SKOULOU change au quotidien."),
        (Direction, "Direction", "Préparer l'établissement, suivre les notes, les bulletins et les paiements."),
        (Enseignant, "Enseignant", "Créer leçons et examens, suivre la classe et noter."),
        (Eleve, "Élève", "Apprendre, s'exercer et passer les examens."),
        (Parent, "Parent", "Suivre la scolarité de vos enfants."),
    ];

    /// <summary>Gruppe der eigenen Rolle (wird nach « Pourquoi SKOULOU ? » zuerst gezeigt).</summary>
    public static string EigeneGruppe(ClaimsPrincipal user)
        => user.IsInRole(Rollen.Lehrer) && !Leitung.Any(user.IsInRole) ? Enseignant
           : user.IsInRole(Rollen.Schueler) ? Eleve
           : user.IsInRole(Rollen.Eltern) ? Parent
           : Direction;

    /// <summary>Sichtbare Videos gruppiert: « Pourquoi SKOULOU ? » zuerst, dann die eigene Rolle, danach die übrigen.</summary>
    public static IReadOnlyList<Gruppe> FuerNutzer(ClaimsPrincipal user)
    {
        var eigene = EigeneGruppe(user);
        return GruppenInfo
            .Select(g => new Gruppe(g.Fuer, g.Titel, g.Untertitel, Videos.Where(v => v.Fuer == g.Fuer && v.DarfSehen(user)).ToList()))
            .Where(g => g.Videos.Count > 0)
            .OrderBy(g => g.Fuer == PourTous ? 0 : g.Fuer == eigene ? 1 : 2)
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
