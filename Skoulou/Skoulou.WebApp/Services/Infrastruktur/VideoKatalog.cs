using System.Security.Claims;
using System.Text.RegularExpressions;
using Skoulou.Core.Rechte;

namespace Skoulou.WebApp.Services.Infrastruktur;

/// <summary>
/// Lernvideos je Rolle plus « Pour bien commencer » für alle; Werbevideo öffentlich.
/// Die Leitung (Admin, Responsable, Direktion) sieht alle, Lehrkräfte ihre und die der Schüler,
/// Schüler und Eltern nur ihre eigenen. Dateien: <c>Medien/videos</c>, Poster: <c>wwwroot/videos</c>.
/// </summary>
public static partial class VideoKatalog
{
    public sealed record LernVideo(string Datei, string Titel, string Beschreibung, string Fuer, IReadOnlyList<string> Rollen, string Dauer)
    {
        public string Url => "/medien/videos/" + Datei;
        public string Poster => "videos/" + Path.ChangeExtension(Datei, ".jpg");
        public bool DarfSehen(ClaimsPrincipal user) => Rollen.Any(user.IsInRole);
    }

    /// <summary>Gruppe auf der Seite « Vidéos d'aide ».</summary>
    public sealed record Gruppe(string Fuer, string Titel, string Untertitel, IReadOnlyList<LernVideo> Videos);

    public const string Werbung = "skoulou-presentation.mp4";
    public const string PourTous = "Pour tous";
    public const string Direction = "Direction";
    public const string Enseignant = "Enseignant";
    public const string Eleve = "Élève";
    public const string Parent = "Parent";

    private static readonly string[] Leitung = [Rollen.SuperAdmin, Rollen.Responsable, Rollen.Direktor, Rollen.Koordinator];
    private static readonly string[] Alle = [.. Leitung, Rollen.Lehrer, Rollen.Schueler, Rollen.Eltern];
    private static readonly string[] FuerLehrkraefte = [.. Leitung, Rollen.Lehrer];
    private static readonly string[] FuerSchueler = [.. Leitung, Rollen.Lehrer, Rollen.Schueler];
    private static readonly string[] FuerEltern = [.. Leitung, Rollen.Eltern];

    public static readonly IReadOnlyList<LernVideo> Videos =
    [
        new("skoulou-visite-guidee.mp4", "La visite guidée",
            "Où trouver chaque fonction : direction, enseignants, élèves et parents.", PourTous, Alle, "2:03"),
        new("skoulou-pourquoi.mp4", "Pourquoi SKOULOU ?",
            "Élèves, notes, bulletins vérifiables, paiements et parents : tout votre établissement dans un seul espace.", PourTous, Alle, "1:39"),
        new("skoulou-direction-preparer.mp4", "Préparer l'année scolaire",
            "L'année et ses périodes, les niveaux et les classes, les matières et leurs coefficients, les composantes des moyennes, puis le démarrage de l'année.",
            Direction, Leitung, "5:54"),
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
        (Direction, "Direction", "Préparer l'année scolaire, l'équipe, les notes, les bulletins et les frais."),
        (Enseignant, "Enseignant", "Créer leçons et examens, suivre la classe et noter."),
        (Eleve, "Élève", "Apprendre, s'exercer et passer les examens."),
        (Parent, "Parent", "Suivre la scolarité de vos enfants."),
    ];

    [GeneratedRegex(@"^[a-z0-9-]+\.mp4$")]
    public static partial Regex DateiMuster();

    /// <summary>Gruppe der eigenen Rolle (erscheint nach « Pour bien commencer »).</summary>
    public static string EigeneGruppe(ClaimsPrincipal user)
        => user.IsInRole(Rollen.Lehrer) ? Enseignant
           : user.IsInRole(Rollen.Schueler) ? Eleve
           : user.IsInRole(Rollen.Eltern) ? Parent
           : Direction;

    /// <summary>Sichtbare Videos gruppiert: « Pour tous » zuerst, dann die eigene Rolle, danach die übrigen.</summary>
    public static IReadOnlyList<Gruppe> FuerNutzer(ClaimsPrincipal user)
    {
        var eigene = EigeneGruppe(user);
        return GruppenInfo
            .Select(g => new Gruppe(g.Fuer, g.Titel, g.Untertitel, Videos.Where(v => v.Fuer == g.Fuer && v.DarfSehen(user)).ToList()))
            .Where(g => g.Videos.Count > 0)
            .OrderBy(g => g.Fuer == PourTous ? 0 : g.Fuer == eigene ? 1 : 2)
            .ToList();
    }

    /// <summary>Adresse mit Versionsnummer aus dem Dateidatum (nach einem Deploy sofort die neue Fassung).</summary>
    public static string MitVersion(string url, string? pfad)
        => pfad is not null && File.Exists(pfad) ? $"{url}?v={File.GetLastWriteTimeUtc(pfad).Ticks:x}" : url;

    public static LernVideo? Finde(string datei) => Videos.FirstOrDefault(v => string.Equals(v.Datei, datei, StringComparison.OrdinalIgnoreCase));

    /// <summary>Darf dieser Nutzer die Datei abrufen? (Werbevideo immer, Lernvideos nach Rolle, Unbekanntes nie.)</summary>
    public static bool DarfAbrufen(string datei, ClaimsPrincipal user)
        => string.Equals(datei, Werbung, StringComparison.OrdinalIgnoreCase)
           || (user.Identity?.IsAuthenticated == true && Finde(datei)?.DarfSehen(user) == true);

    /// <summary>Range-fähiger Video-Endpunkt <c>/medien/videos/{datei}</c>.</summary>
    public static IEndpointRouteBuilder MapVideoEndpunkt(this IEndpointRouteBuilder app, string videoOrdner)
    {
        app.MapGet("/medien/videos/{datei}", (string datei, HttpContext http) =>
        {
            if (!DateiMuster().IsMatch(datei)) return Results.NotFound();
            if (!DarfAbrufen(datei, http.User))
                return http.User.Identity?.IsAuthenticated == true ? Results.StatusCode(StatusCodes.Status403Forbidden) : Results.Unauthorized();
            var info = new FileInfo(Path.Combine(videoOrdner, datei));
            if (!info.Exists) return Results.NotFound();
            var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}\"");
            http.Response.Headers.CacheControl = string.Equals(datei, Werbung, StringComparison.OrdinalIgnoreCase)
                ? "public, max-age=3600" : "private, max-age=3600";
            return Results.File(info.FullName, "video/mp4", lastModified: info.LastWriteTimeUtc, entityTag: etag, enableRangeProcessing: true);
        });
        return app;
    }
}
