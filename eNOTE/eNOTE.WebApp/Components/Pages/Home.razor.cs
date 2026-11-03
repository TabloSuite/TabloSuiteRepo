using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Home(KatalogService katalogService, AuthenticationStateProvider authStateProvider)
{
    private bool wartetAufFreigabe;
    private string? salonNameWartend;

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
        }
    }
}
