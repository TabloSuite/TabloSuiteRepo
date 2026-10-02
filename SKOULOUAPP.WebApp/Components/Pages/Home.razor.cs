using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class Home
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    protected override void OnInitialized()
    {
        if (HttpContext?.User?.Identity?.IsAuthenticated == true && HttpContext.User.IsInRole("Schüler"))
        {
            Navigation.NavigateTo("/student", replace: true);
        }
    }
}

