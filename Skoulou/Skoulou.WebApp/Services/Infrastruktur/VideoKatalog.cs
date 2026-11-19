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
    public const string Responsable = "Responsable";
    public const string Administrateur = "Administrateur";

    private static readonly string[] Leitung = [Rollen.SuperAdmin, Rollen.Responsable, Rollen.Direktor, Rollen.Koordinator];
    private static readonly string[] Alle = [.. Leitung, Rollen.Lehrer, Rollen.Schueler, Rollen.Eltern];
    private static readonly string[] FuerLehrkraefte = [.. Leitung, Rollen.Lehrer];
    private static readonly string[] FuerSchueler = [.. Leitung, Rollen.Lehrer, Rollen.Schueler];
    private static readonly string[] FuerEltern = [.. Leitung, Rollen.Eltern];
    private static readonly string[] FuerResponsables = [Rollen.SuperAdmin, Rollen.Responsable];
    private static readonly string[] NurAdmin = [Rollen.SuperAdmin];

    public static readonly IReadOnlyList<LernVideo> Videos =
    [
        new("skoulou-acces-inscription.mp4", "S'inscrire et retrouver son accès",
            "Inscrire votre établissement et attendre sa validation, l'inscription d'un élève par lui-même, le mot de passe oublié, les nouveaux accès pour toute une classe, et la page Contact.",
            PourTous, Alle, "4:10"),
        new("skoulou-premiers-pas.mp4", "Premiers pas : se repérer",
            "Se connecter, choisir son mot de passe, l'accueil, le menu sur ordinateur et sur téléphone, l'année scolaire, votre compte et l'aide.",
            PourTous, Alle, "5:20"),
        new("skoulou-visite-guidee.mp4", "La visite guidée",
            "Qui voit quoi : la direction, la coordination, l'enseignant, l'élève et le parent, chacun avec son accueil et son menu.", PourTous, Alle, "3:57"),
        new("skoulou-pourquoi.mp4", "Pourquoi SKOULOU ?",
            "Les soucis de fin de trimestre, puis la preuve dans l'application : des moyennes justes, des bulletins vérifiables par QR code, des parents informés, une caisse claire et l'école d'un coup d'œil.", PourTous, Alle, "3:07"),
        new("skoulou-direction-concept.mp4", "Le concept : l'année scolaire",
            "La théorie avant la pratique : le cycle d'une année, les périodes, les niveaux et les classes, les matières et les coefficients, le calcul des moyennes, les bulletins et le passage d'année.",
            Direction, Leitung, "6:23"),
        new("skoulou-direction-preparer.mp4", "Préparer l'année scolaire",
            "L'année et ses périodes, les niveaux et les classes, les matières et leurs coefficients, les composantes des moyennes, puis le démarrage de l'année.",
            Direction, Leitung, "6:20"),
        new("skoulou-direction-langues.mp4", "Une école de langues : l'année en sessions",
            "Pour les écoles de langues et les formations courtes : l'année en sessions de trois mois, une classe par niveau et par session, les frais par session, l'examen final et son bulletin, le passage au niveau suivant, et la facture SKOULOU par session.",
            Direction, Leitung, "5:22"),
        new("skoulou-direction-eleves.mp4", "Élèves et inscriptions",
            "La liste des élèves, inscrire un élève, importer depuis Excel, valider les inscriptions en ligne, changer de classe, le départ, et les accès des élèves et des parents.",
            Direction, Leitung, "4:42"),
        new("skoulou-direction-etablissement.mp4", "Votre établissement",
            "L'identité de l'école, l'en-tête des bulletins et des reçus, le logo, les réglages (matricules, absences, notes visibles des parents) et les droits des coordinateurs – avec leur effet sur les documents imprimés.",
            Direction, Leitung, "3:11"),
        new("skoulou-direction-cartes.mp4", "Les cartes d'élève",
            "Imprimer les cartes d'élève d'une classe, lire la carte, vérifier son code QR avec un téléphone, et renouveler une carte perdue.",
            Direction, Leitung, "1:58"),
        new("skoulou-direction-equipe.mp4", "Équipe et emplois du temps",
            "Les comptes des enseignants et des coordinateurs, les affectations, les matières et les enseignants de chaque classe, l'emploi du temps avec le contrôle des conflits, puis la publication.",
            Direction, Leitung, "4:30"),
        new("skoulou-direction-notes.mp4", "Notes et conseil de classe",
            "Saisir les notes, les appréciations et les absences, préparer le conseil de classe – et voir, pour chaque saisie, où elle apparaît sur le bulletin imprimé.",
            Direction, Leitung, "5:17"),
        new("skoulou-direction-bulletins.mp4", "Publier les bulletins",
            "Publier les bulletins, lire le bulletin imprimé case par case, consigner la remise, vérifier le code QR, rectifier une erreur, et adapter le modèle de bulletin.",
            Direction, Leitung, "5:07"),
        new("skoulou-direction-facultatives.mp4", "Les matières facultatives",
            "Une matière que seuls certains élèves suivent, comme l'ewe ou la culture : la rendre facultative, choisir sa règle pour la moyenne, cocher les élèves, les notes, le bulletin, et ce qui est fixé après la publication.",
            Direction, Leitung, "5:37"),
        new("skoulou-direction-passage.mp4", "Fin d'année et passage",
            "Les décisions de fin d'année au conseil, le bilan annuel sur le dernier bulletin, puis le passage à l'année suivante : nouvelle année, classes recréées, admis au niveau suivant, redoublants, départs – en une seule étape.",
            Direction, Leitung, "2:58"),
        new("skoulou-direction-fermer.mp4", "Fermer et rouvrir",
            "Fermer une période et la rouvrir pour une note oubliée, retirer un bulletin avec un motif, modifier un emploi du temps déjà publié, clôturer l'année et la rouvrir avec un motif.",
            Direction, Leitung, "3:54"),
        new("skoulou-direction-contenus.mp4", "Contenus des enseignants et journal",
            "Voir toutes les leçons et tous les examens de l'établissement, saisir un contenu pour un enseignant sans appareil, et suivre dans le journal qui a fait quoi, et quand.",
            Direction, Leitung, "1:53"),
        new("skoulou-direction-concept-frais.mp4", "Le concept : frais et paiements",
            "La théorie avant la pratique : les frais et leurs tranches, les réductions, les paiements et les reçus, les annulations, le journal de caisse, et la facture SKOULOU.",
            Direction, Leitung, "4:16"),
        new("skoulou-direction-frais.mp4", "Frais de scolarité et paiements",
            "Les frais et leurs tranches, le compte de chaque élève, une réduction, encaisser par Mobile Money avec le reçu, annuler un paiement, le journal de caisse et le rapport financier.",
            Direction, Leitung, "4:20"),
        new("skoulou-direction-finances.mp4", "Finances : vue d'ensemble, catégories et reprise des frais",
            "La vue d'ensemble des finances, un frais facultatif et l'exonération d'un élève, vos catégories du journal de caisse, corriger une écriture par une écriture inverse, et reprendre les frais pour l'année suivante.",
            Direction, Leitung, "3:02"),
        new("skoulou-direction-facture.mp4", "La facture SKOULOU",
            "Comment SKOULOU calcule votre facture (prix par élève et par mois, figé au démarrage, facturé à la fin de chaque période selon ses mois ; qui est compté), la facture et son PDF, contester, déclarer le paiement, le journal de caisse.",
            Direction, Leitung, "4:14"),
        new("skoulou-enseignant-concept.mp4", "Le concept : leçons, exercices et examens",
            "La théorie avant la pratique : leçon ou examen, les pages et les exercices, les huit types de questions, l'attribution à une classe, l'examen chronométré, la correction automatique et le score de l'élève.",
            Enseignant, FuerLehrkraefte, "3:47"),
        new("skoulou-enseignant-cours.mp4", "Leçons et examens",
            "Créer une leçon avec ses pages et ses exercices, une dictée audio avec la langue et le niveau, l'attribuer à une classe, puis l'examen et la correction des réponses libres.",
            Enseignant, FuerLehrkraefte, "3:44"),
        new("skoulou-enseignant-contenus.mp4", "Gérer vos contenus",
            "Une image de la médiathèque et une formule dans une leçon, réutiliser une leçon par copie, l'imprimer avec ou sans corrigé, modifier une date limite, archiver une attribution ou une leçon – et le transfert par la direction.",
            Enseignant, FuerLehrkraefte, "3:02"),
        new("skoulou-enseignant-notes.mp4", "Suivre la classe : appel, notes et bulletin",
            "Votre emploi du temps, l'appel avec les absences et les retards, le récapitulatif, une nouvelle évaluation et la saisie des notes, les appréciations, le conseil de votre classe – et ce que tout cela devient sur le bulletin imprimé.",
            Enseignant, FuerLehrkraefte, "4:30"),
        new("skoulou-eleve-apprendre.mp4", "Apprendre avec SKOULOU",
            "Votre accueil, une leçon et ses exercices, une dictée audio, un examen, vos résultats et vos bulletins.", Eleve, FuerSchueler, "3:13"),
        new("skoulou-parent-suivre.mp4", "Suivre votre enfant",
            "Sur votre téléphone : vos enfants, les notes et les bulletins, les absences et les retards, l'emploi du temps, les frais et les reçus.", Parent, FuerEltern, "2:41"),
        new("skoulou-responsable-ecoles.mp4", "Responsable : accompagner vos écoles",
            "Vos écoles en un coup d'œil et leur période d'essai, leur facturation SKOULOU, vos parts, la messagerie avec les directions, et votre formation.",
            Responsable, FuerResponsables, "2:59"),
        new("skoulou-admin-ecoles.mp4", "Administrateur : écoles et comptes",
            "Le tableau de bord, la validation des écoles, leur responsable, la période d'essai, les comptes de la plateforme, les responsables et les administrateurs.",
            Administrateur, NurAdmin, "3:05"),
        new("skoulou-admin-facturation.mp4", "Administrateur : facturation, tarifs et parts",
            "Les tarifs (prix mensuel par élève, délai, parts), facturer maintenant, confirmer un paiement, répondre à une contestation, verser les parts des responsables.",
            Administrateur, NurAdmin, "2:43"),
        new("skoulou-admin-communication.mp4", "Administrateur : annonces, messagerie et surveillance",
            "Publier une annonce, traiter la messagerie, suivre l'audit des actions sensibles, le journal des erreurs et les médias.",
            Administrateur, NurAdmin, "1:45"),
        new("skoulou-admin-formation.mp4", "Administrateur : la formation des responsables",
            "Ce que voit le responsable (leçons, évaluation, résultat), puis votre suivi : intervalle, délai, dispense, pause, historique, retrait du rôle avec ses écoles, règles de l'évaluation et votre entraînement.",
            Administrateur, NurAdmin, "3:25"),
    ];

    private static readonly (string Fuer, string Titel, string Untertitel)[] GruppenInfo =
    [
        (PourTous, "Pour bien commencer", "S'inscrire et retrouver son accès, les premiers pas, la visite guidée et pourquoi SKOULOU."),
        (Direction, "Direction", "Le concept de l'année, puis la pratique : préparer l'année, l'établissement, les élèves, l'équipe, les notes, les bulletins, le passage d'année, fermer et rouvrir, les frais et la facture SKOULOU."),
        (Enseignant, "Enseignant", "Suivre la classe et noter, puis le concept et la pratique des leçons et des examens, et la gestion de vos contenus."),
        (Eleve, "Élève", "Apprendre, s'exercer et passer les examens."),
        (Parent, "Parent", "Suivre la scolarité de vos enfants."),
        (Responsable, "Responsable", "Accompagner vos écoles : suivi, facturation, parts et messagerie."),
        (Administrateur, "Administrateur", "Écoles et comptes, facturation et tarifs, annonces et surveillance, la formation des responsables."),
    ];

    [GeneratedRegex(@"^[a-z0-9-]+\.mp4$")]
    public static partial Regex DateiMuster();

    /// <summary>Gruppe der eigenen Rolle (erscheint nach « Pour bien commencer »).</summary>
    public static string EigeneGruppe(ClaimsPrincipal user)
        => user.IsInRole(Rollen.SuperAdmin) ? Administrateur
           : user.IsInRole(Rollen.Responsable) ? Responsable
           : user.IsInRole(Rollen.Lehrer) ? Enseignant
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
