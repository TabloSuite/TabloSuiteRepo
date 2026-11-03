using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Profil(AuthService authService, AuthenticationStateProvider authStateProvider, NavigationManager navigationManager)
{
    private bool isLoading = true;
    private Guid userId;
    private string fullName = string.Empty;
    private string email = string.Empty;
    private List<string> rollen = [];

    private string aktuellesPasswort = string.Empty;
    private string neuesPasswort = string.Empty;
    private string? pwFehler;
    private bool pwGeaendert;

    private string Initialen => string.Concat(fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => char.ToUpperInvariant(t[0])).Take(2));

    protected override async Task OnInitializedAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;
        var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out userId))
        {
            isLoading = false;
            return;
        }

        fullName = user.FindFirstValue(ClaimTypes.GivenName) ?? user.Identity?.Name ?? "";
        email = user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name ?? "";
        rollen = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            fullName = email;
        }

        isLoading = false;
    }

    private async Task AenderePasswortAsync()
    {
        pwFehler = null;
        pwGeaendert = false;

        if (string.IsNullOrWhiteSpace(aktuellesPasswort) || string.IsNullOrWhiteSpace(neuesPasswort))
        {
            pwFehler = "Veuillez remplir les deux champs.";
            return;
        }

        var (erfolg, fehler) = await authService.AenderePasswortAsync(userId, aktuellesPasswort, neuesPasswort);
        if (!erfolg)
        {
            pwFehler = fehler;
            return;
        }

        pwGeaendert = true;
        // Session rotiert → zur Anmeldung weiterleiten.
        navigationManager.NavigateTo("/auth/login?info=password-updated", forceLoad: true);
    }

    private static string RolleText(string rolle) => rolle switch
    {
        "SuperAdmin" => "Administrateur",
        "ProduktOwner" => "Propriétaire plateforme",
        "Besitzer" => "Propriétaire",
        "Kassierer" => "Caissier",
        _ => rolle
    };
}
