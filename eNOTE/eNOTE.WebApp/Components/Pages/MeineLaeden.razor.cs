using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class MeineLaeden(ProduktOwnerService produktOwnerService, AuthenticationStateProvider authStateProvider)
{
    private List<ProduktOwnerService.DashboardLaden> laeden = [];
    private bool isLoading = true;

    protected override async Task OnInitializedAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            isLoading = false;
            return;
        }

        laeden = await produktOwnerService.GetDashboardAsync(userId);
        isLoading = false;
    }

    private static string ProvisionText(ProduktOwnerService.DashboardLaden l)
    {
        var teile = new List<string>();
        if (l.ProzentProvision is not null)
        {
            teile.Add($"{l.ProzentProvision:N0} %");
        }

        if (l.FestbetragProvision is not null)
        {
            teile.Add($"{l.FestbetragProvision:N0} F");
        }

        return string.Join(" + ", teile);
    }
}
