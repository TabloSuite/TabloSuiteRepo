using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Home(
    KatalogService katalogService,
    HinweisService hinweisService,
    AuthenticationStateProvider authStateProvider)
{
    private bool wartetAufFreigabe;
    private bool hinweiseGeladen;
    private string? salonNameWartend;
    private List<ENote.Core.DataModels.Kasse.BesitzerHinweis> ungeleseneHinweise = [];

    protected override async Task OnInitializedAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return;
        }

        var zuordnungen = await katalogService.GetUserSalonsAsync(userId);
        if (zuordnungen.Count == 0)
        {
            return;
        }

        // Wartet nur, wenn es keinen freigegebenen Salon gibt, aber mindestens einen wartenden.
        var hatFreigegebenen = zuordnungen.Any(z => z.Salon.IstFreigegeben);
        if (!hatFreigegebenen)
        {
            wartetAufFreigabe = true;
            salonNameWartend = zuordnungen.First().Salon.Name;
            return;
        }

        // Ungelesene Hinweise für den Besitzer laden (roter Hinweis).
        var istBesitzer = authState.User.IsInRole(Core.DataModels.Identity.AppRollen.Besitzer);
        if (istBesitzer)
        {
            var freigegeben = zuordnungen.First(z => z.Salon.IstFreigegeben);
            ungeleseneHinweise = await hinweisService.GetUngeleseneAsync(freigegeben.SalonId);
            hinweiseGeladen = true;
        }
    }

    private async Task MarkiereGelesenAsync(Guid id)
    {
        await hinweisService.MarkiereGelesenAsync(id);
        ungeleseneHinweise.RemoveAll(h => h.Id == id);
    }

    private async Task MarkiereAlleGelesenAsync()
    {
        if (ungeleseneHinweise.Count == 0)
        {
            return;
        }

        var salonId = ungeleseneHinweise.First().SalonId;
        await hinweisService.MarkiereAlleGelesenAsync(salonId);
        ungeleseneHinweise.Clear();
    }
}
