using System.Security.Claims;
using HalloApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace HalloApp.WebApp.Components.Layout;

public partial class NavMenu
{
    [Inject] private FinanzService FinanzService { get; set; } = default!;
    [CascadingParameter] private Task<AuthenticationState>? AuthStateTask { get; set; }

    private bool coordinatorHasFinanceAccess;

    protected override async Task OnParametersSetAsync()
    {
        if (AuthStateTask is null) return;
        var user = (await AuthStateTask).User;
        if (!user.IsInRole("Koordinator") || user.IsInRole("SchulLeiter")) return;
        if (Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            coordinatorHasFinanceAccess = (await FinanzService.GetSchulenAsync(userId, false)).Count > 0;
        }
    }
}
