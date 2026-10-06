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

