using System.Security.Claims;
using SkoulouApp.Core.DataModels.Identity;

namespace SkoulouApp.WebApp.Services;

/// <summary>
/// Lernvideos je Rolle (Direction, Enseignant, Élève, Parent) plus « Pour bien commencer » (Visite guidée, Pourquoi SKOULOU ?) für alle.
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
        // Seit 12.11.2026: Lernvideos v2 mit Sprecherin (am Anfang, in « En résumé » und am Schluss), Schritt für Schritt
        new("skoulou-visite-guidee.mp4", "La visite guidée",
            "Où trouver chaque fonction : direction, enseignants, élèves et parents.", PourTous, Alle, "2:03"),
        new("skoulou-pourquoi.mp4", "Pourquoi SKOULOU ?",
            "Direction, enseignants, élèves et parents : ce que SKOULOU change au quotidien.", PourTous, Alle, "0:47"),

        new("skoulou-direction-preparer.mp4", "Préparer l'établissement",
            "L'année scolaire, les classes, inscrire un élève, un nouveau mot de passe.", Direction, Leitung, "1:50"),
        new("skoulou-direction-equipe.mp4", "Enseignants et emplois du temps",
            "Ajouter un enseignant, lui confier une classe et une matière, publier l'emploi du temps.", Direction, Leitung, "1:37"),
        new("skoulou-direction-notes.mp4", "Notes et bulletins",
            "Saisir ou corriger une note, préparer le bulletin, le publier et le télécharger.", Direction, Leitung, "1:38"),
        new("skoulou-direction-frais.mp4", "Frais de scolarité et parents",
            "Suivre les paiements des élèves, les recettes et les dépenses, créer l'accès des parents.", Direction, Leitung, "1:16"),

        new("skoulou-enseignant-cours.mp4", "Leçons et examens",
            "Créer une leçon avec ses exercices, la publier, créer un examen corrigé automatiquement.", Enseignant, FuerLehrkraefte, "1:27"),
        new("skoulou-enseignant-notes.mp4", "Suivre la classe et noter",
            "Le tableau pédagogique, votre emploi du temps, saisir les notes.", Enseignant, FuerLehrkraefte, "1:09"),

        new("skoulou-eleve-apprendre.mp4", "Apprendre avec SKOULOU",
            "Votre espace, les leçons et les exercices, les examens et la correction.", Eleve, FuerSchueler, "1:06"),

        new("skoulou-parent-suivre.mp4", "Suivre votre enfant",
            "Bulletins publiés, absences et retards, frais de scolarité et reçus.", Parent, FuerEltern, "0:42"),
    ];

    private static readonly (string Fuer, string Titel, string Untertitel)[] GruppenInfo =
    [
        (PourTous, "Pour bien commencer", "La visite guidée et pourquoi SKOULOU."),
        (Direction, "Direction", "Préparer l'établissement, l'équipe, les notes, les bulletins et les frais."),
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
