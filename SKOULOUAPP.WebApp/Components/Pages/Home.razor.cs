using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SkoulouApp.WebApp.Data;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class Home
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IDbContextFactory<ApplicationDbContext> DbContextFactory { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    private int? aktiveSchulenAnzahl;
    private int? aktiveSchuelerAnzahl;

    private static readonly TimeSpan CacheDauer = TimeSpan.FromMinutes(10);
    private static volatile ZaehlerCache? zaehlerCache;

    private sealed record ZaehlerCache(int Schulen, int Schueler, DateTime ErstelltUtc);

    private static readonly (string Icon, string Titel, string Text)[] Funktionen =
    [
        ("users", "Élèves et classes", "Inscriptions, matricules, affectation aux classes et import depuis un fichier. Chaque élève reçoit son identifiant de connexion."),
        ("timetable", "Enseignants et emplois du temps", "Matières, affectations des enseignants et emplois du temps par classe, publiés pour toute l'école."),
        ("report", "Notes et bulletins", "Saisie des notes par période, moyennes et rangs, bulletins PDF avec QR code de vérification."),
        ("wallet", "Frais de scolarité", "Frais par classe, paiements en une fois ou en plusieurs tranches, suivi des soldes restants."),
        ("book-open", "Cours et exercices", "Leçons, exercices et examens en ligne, avec suivi de la progression de chaque élève."),
        ("bars", "Comptabilité et rapports", "Recettes et dépenses de l'établissement, rapports financiers clairs, exportables en PDF.")
    ];

    private static readonly (string Icon, string Rolle, string[] Punkte)[] Rollen =
    [
        ("school", "Direction", ["Vue d'ensemble de l'établissement", "Comptes des enseignants et des élèves", "Bulletins et finances sous contrôle"]),
        ("pen", "Enseignants", ["Notes et appréciations par classe", "Leçons, exercices et examens", "Emploi du temps toujours à jour"]),
        ("graduation", "Élèves", ["Cours et exercices de la classe", "Bulletins consultables en ligne", "Progression visible à tout moment"])
    ];

    private static readonly (int Nummer, string Schritt, string Erklaerung)[] Schritte =
    [
        (1, "Inscription", "Créez le compte de direction et votre établissement."),
        (2, "Validation", "SKOULOU vérifie et active votre établissement."),
        (3, "Équipe et élèves", "Ajoutez enseignants et élèves, un par un ou par fichier.")
    ];

    protected override async Task OnInitializedAsync()
    {
        if (HttpContext?.User?.Identity?.IsAuthenticated == true && HttpContext.User.IsInRole("Schüler"))
        {
            Navigation.NavigateTo("/student", replace: true);
            return;
        }

        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        if (authState.User.Identity?.IsAuthenticated == true)
        {
            return;
        }

        try
        {
            var cache = zaehlerCache;
            if (cache is not null && DateTime.UtcNow - cache.ErstelltUtc < CacheDauer)
            {
                aktiveSchulenAnzahl = cache.Schulen;
                aktiveSchuelerAnzahl = cache.Schueler;
                return;
            }

            await using var dbContext = await DbContextFactory.CreateDbContextAsync();
            aktiveSchulenAnzahl = await dbContext.Schools.AsNoTracking().CountAsync(x => x.IsActive);
            aktiveSchuelerAnzahl = await dbContext.StudentProfiles.AsNoTracking()
                .CountAsync(x => !x.IsArchived && x.School.IsActive && !x.AppUser.IsBlocked);
            zaehlerCache = new ZaehlerCache(aktiveSchulenAnzahl.Value, aktiveSchuelerAnzahl.Value, DateTime.UtcNow);
        }
        catch (Exception)
        {
            aktiveSchulenAnzahl = null;
            aktiveSchuelerAnzahl = null;
        }
    }
}

