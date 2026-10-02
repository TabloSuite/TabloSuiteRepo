using Microsoft.AspNetCore.Components;

namespace SkoulouApp.WebApp.Components.Auth;

public partial class RedirectToLogin
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    protected override void OnInitialized()
    {
        var returnUrl = Uri.EscapeDataString(NavigationManager.ToBaseRelativePath(NavigationManager.Uri));
        NavigationManager.NavigateTo($"/auth/login?returnUrl={returnUrl}", forceLoad: true);
    }
}
