using System.Security.Claims;
using HalloApp.Core.DataModels.Identity;
using HalloApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace HalloApp.WebApp.Components.Layout;

public partial class TermsAcceptanceGuard
{
    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private AuthService AuthService { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    private bool showTermsModal;
    private bool isAcceptingTerms;
    private string? termsErrorMessage;
    private string? termsSectionToScroll;
    private Guid currentUserId;
    private string currentRole = Rollen.Schueler;

    protected override async Task OnInitializedAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var user = authenticationState.User;
        var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Identity?.IsAuthenticated != true || !Guid.TryParse(userIdValue, out currentUserId))
        {
            return;
        }

        currentRole = GetPrimaryRole(user);
        showTermsModal = await AuthService.RequiresTermsAcceptanceAsync(currentUserId);
        if (showTermsModal)
        {
            termsSectionToScroll = GetTermsSectionId(currentRole);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!string.IsNullOrWhiteSpace(termsSectionToScroll))
        {
            var sectionId = termsSectionToScroll;
            termsSectionToScroll = null;
            await JS.InvokeVoidAsync("SKOULOUAPP.scrollIntoView", sectionId);
        }
    }

    private async Task AcceptTermsAsync()
    {
        if (isAcceptingTerms)
        {
            return;
        }

        isAcceptingTerms = true;
        termsErrorMessage = null;
        try
        {
            if (await AuthService.AcceptTermsAsync(currentUserId))
            {
                showTermsModal = false;
                return;
            }

            termsErrorMessage = "Les conditions n'ont pas pu être enregistrées. Réessaie.";
        }
        finally
        {
            isAcceptingTerms = false;
        }
    }

    private static string GetPrimaryRole(ClaimsPrincipal user)
    {
        if (user.IsInRole(Rollen.ProduktOwner)) return Rollen.ProduktOwner;
        if (user.IsInRole(Rollen.Koordinator)) return Rollen.Koordinator;
        if (user.IsInRole(Rollen.SchulLeiter)) return Rollen.SchulLeiter;
        if (user.IsInRole(Rollen.Lehrer)) return Rollen.Lehrer;
        return Rollen.Schueler;
    }

    private static string GetTermsSectionId(string role) => role switch
    {
        Rollen.SchulLeiter or Rollen.Koordinator => "terms-school",
        Rollen.Lehrer => "terms-teacher",
        Rollen.ProduktOwner => "terms-owner",
        _ => "terms-student"
    };
}
