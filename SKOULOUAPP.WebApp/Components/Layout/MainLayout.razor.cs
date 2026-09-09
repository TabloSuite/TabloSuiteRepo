using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace HalloApp.WebApp.Components.Layout;

public partial class MainLayout
{
    [CascadingParameter]
    private Task<AuthenticationState>? AuthStateTask { get; set; }

    private bool isStudentChromeHidden;

    protected override async Task OnParametersSetAsync()
    {
        if (AuthStateTask is not null)
        {
            var authState = await AuthStateTask;
            isStudentChromeHidden = authState.User.Identity?.IsAuthenticated == true
                && authState.User.IsInRole("Schüler");
        }
    }
}
