using System.Security.Claims;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.Models.Handbuch;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace HalloApp.WebApp.Components.Pages;

public partial class Handbuch
{
    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    private string activeTab = "start";
    private string currentRole = Rollen.Schueler;

    private IReadOnlyList<GuideTab> VisibleTabs
    {
        get
        {
            return HandbuchTabSichtbarkeit.GetTabIds(currentRole)
                .Select(tabId => HandbuchContent.TabDefinitions[tabId])
                .Select(tab => new GuideTab(tab.Id, tab.Title, tab.Icon))
                .ToList();
        }
    }

    private IReadOnlyList<GuideStartStep> StartSteps => HandbuchContent.GetStartSteps(currentRole);

    private string RoleLabel => HandbuchContent.GetRoleLabel(currentRole);

    private IReadOnlyDictionary<string, HandbuchPanel> Panels => HandbuchContent.GetPanels(currentRole);

    private HandbuchPanel? ActivePanel => Panels.TryGetValue(activeTab, out var panel) ? panel : null;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        currentRole = GetPrimaryRole(state.User);
    }

    private static string GetPrimaryRole(ClaimsPrincipal user)
    {
        if (user.IsInRole(Rollen.SuperAdmin)) return Rollen.SuperAdmin;
        if (user.IsInRole(Rollen.ProduktOwner)) return Rollen.ProduktOwner;
        if (user.IsInRole(Rollen.Koordinator)) return Rollen.Koordinator;
        if (user.IsInRole(Rollen.SchulLeiter)) return Rollen.SchulLeiter;
        if (user.IsInRole(Rollen.Lehrer)) return Rollen.Lehrer;
        return Rollen.Schueler;
    }

    private sealed record GuideTab(string Id, string Title, string Icon);
    private sealed record GuideStep(string Number, string Title, string Description);
}
