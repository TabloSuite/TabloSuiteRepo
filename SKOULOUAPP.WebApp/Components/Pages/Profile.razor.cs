using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using HalloApp.Core.DataModels.Identity;
using HalloApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace HalloApp.WebApp.Components.Pages;

public partial class Profile
{
    [Inject]
    private AuthService AuthService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private DatabaseActionGuard DatabaseActionGuard { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private Guid userId;
    private string? email;
    private bool isLoading = true;

    private ProfileInput? profileModel;

    [SupplyParameterFromForm(FormName = "profile-form")]
    private ProfileInput ProfileModel
    {
        get => profileModel ??= new();
        set => profileModel = value;
    }

    private PasswordChangeInput? passwordModel;

    [SupplyParameterFromForm(FormName = "change-password-form")]
    private PasswordChangeInput PasswordModel
    {
        get => passwordModel ??= new();
        set => passwordModel = value;
    }

    private string? profileMessage;
    private bool profileSucceeded;
    private bool isSavingProfile;

    private string? passwordMessage;
    private bool passwordSucceeded;
    private bool isSavingPassword;

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var value = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(value, out userId))
        {
            isLoading = false;
            return;
        }

        var user = await AuthService.GetUserAsync(userId);
        if (user is not null)
        {
            email = user.Email;
            // Nur befüllen, wenn kein Formular-Postback aktiv ist.
            if (profileModel is null)
            {
                profileModel = new ProfileInput
                {
                    Vorname = user.Vorname,
                    Nachname = user.Nachname,
                    Geschlecht = user.Geschlecht
                };
            }
        }

        isLoading = false;
    }

    private async Task HandleProfileAsync()
    {
        if (isSavingProfile)
        {
            return;
        }

        isSavingProfile = true;
        profileMessage = null;
        try
        {
            await DatabaseActionGuard.RunAsync(async () =>
            {
                var result = await AuthService.UpdateProfileAsync(userId, ProfileModel.Vorname, ProfileModel.Nachname, ProfileModel.Geschlecht);
                profileSucceeded = result.Succeeded;
                profileMessage = result.Message;
            });
        }
        finally
        {
            isSavingProfile = false;
        }
    }

    private async Task HandlePasswordAsync()
    {
        if (isSavingPassword)
        {
            return;
        }

        isSavingPassword = true;
        passwordMessage = null;
        var shouldSignOutAfterChange = false;
        try
        {
            await DatabaseActionGuard.RunAsync(async () =>
            {
                var result = await AuthService.ChangePasswordAsync(userId, PasswordModel.CurrentPassword, PasswordModel.NewPassword);
                passwordSucceeded = result.Succeeded;
                passwordMessage = result.Message;
                if (result.Succeeded)
                {
                    passwordModel = new PasswordChangeInput();
                    shouldSignOutAfterChange = true;
                }
            });

            if (shouldSignOutAfterChange)
            {
                await AuthService.SignOutAsync();
                Navigation.NavigateTo("/auth/login?info=password-updated", forceLoad: true);
            }
        }
        finally
        {
            isSavingPassword = false;
        }
    }

    private sealed class ProfileInput
    {
        [Required(ErrorMessage = "Veuillez saisir un prénom.")]
        public string Vorname { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez saisir un nom.")]
        public string Nachname { get; set; } = string.Empty;

        public Geschlecht Geschlecht { get; set; } = Geschlecht.Divers;
    }

    private sealed class PasswordChangeInput
    {
        [Required(ErrorMessage = "Veuillez saisir votre mot de passe actuel.")]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez saisir un nouveau mot de passe.")]
        [DataType(DataType.Password)]
        [MinLength(6, ErrorMessage = "Le mot de passe doit contenir au moins 6 caractères.")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez confirmer le nouveau mot de passe.")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "Les mots de passe ne correspondent pas.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
