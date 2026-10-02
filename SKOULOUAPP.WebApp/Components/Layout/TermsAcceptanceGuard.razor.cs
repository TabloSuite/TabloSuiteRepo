using System.Security.Claims;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace SkoulouApp.WebApp.Components.Layout;

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
    private string currentRoleLabel = "Élève";
    private string currentRoleSummary = "Tu consultes les contenus de ta classe selon les autorisations de ton école.";

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
        currentRoleLabel = GetRoleLabel(currentRole);
        currentRoleSummary = GetRoleSummary(currentRole);
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

    private static string GetRoleLabel(string role) => role switch
    {
        Rollen.SchulLeiter => "Direction d'école",
        Rollen.Koordinator => "Coordinateur",
        Rollen.Lehrer => "Enseignant",
        Rollen.ProduktOwner => "Responsable produit",
        _ => "Élève"
    };

    private static string GetRoleSummary(string role) => role switch
    {
        Rollen.SchulLeiter => "Tu gères les comptes, les classes et les validations de ton établissement.",
        Rollen.Koordinator => "Tu assistes la direction sur les opérations pédagogiques et organisationnelles.",
        Rollen.Lehrer => "Tu publies des leçons, exercices et évaluations pour les classes attribuées.",
        Rollen.ProduktOwner => "Tu suis les écoles attribuées avec un accès de pilotage limité à ton périmètre.",
        _ => "Tu consultes les contenus de ta classe selon les autorisations de ton école."
    };
}
