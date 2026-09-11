using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.Models.Media;
using HalloApp.Core.Models.Profile;
using HalloApp.Core.Models.TeacherLessons;
using HalloApp.WebApp.Components.Shared;
using HalloApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

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

    [Inject]
    private IJSRuntime JsRuntime { get; set; } = default!;

    private Guid userId;
    private string? email;
    private bool isLoading = true;

    private UserProfileModel? fullProfile;
    private bool isTeacher;

    private List<FachOptionItem> teacherFachOptions = [];
    private HashSet<Guid> selectedFachIds = [];
    private string newFachName = string.Empty;
    private bool isAddingFach;
    private bool imageLibraryOpen;
    private string? profileImageUrl;
    private string currentTheme = "navy";

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
    private string? profileToastMessage;
    private Toast.ToastKind profileToastKind = Toast.ToastKind.Success;

    private string? passwordMessage;
    private bool passwordSucceeded;
    private bool isSavingPassword;
    private string? passwordToastMessage;
    private Toast.ToastKind passwordToastKind = Toast.ToastKind.Success;

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var value = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(value, out userId))
        {
            isLoading = false;
            return;
        }

        var loadedProfile = await AuthService.GetUserProfileAsync(userId);

        if (loadedProfile is not null)
        {
            fullProfile = loadedProfile;
            email = loadedProfile.Email;
            isTeacher = loadedProfile.Rollen.Contains(Rollen.Lehrer);
            profileImageUrl = loadedProfile.ProfilBildUrl;

            if (isTeacher)
            {
                teacherFachOptions = (await AuthService.GetTeacherFachOptionsAsync(userId)).ToList();
                selectedFachIds = loadedProfile.Faecher.Select(item => item.FachId).ToHashSet();
            }

            // Nur befüllen, wenn kein Formular-Postback aktiv ist.
            if (profileModel is null)
            {
                profileModel = new ProfileInput
                {
                    Vorname = loadedProfile.Vorname,
                    Nachname = loadedProfile.Nachname,
                    Geschlecht = loadedProfile.Geschlecht,
                    TelefonNummer = loadedProfile.TelefonNummer,
                    ProfilBildAssetId = loadedProfile.ProfilBildAssetId
                };
            }
        }

        isLoading = false;

        try
        {
            var storedTheme = await JsRuntime.InvokeAsync<string>("SKOULOUAPPTheme.get");
            if (!string.IsNullOrWhiteSpace(storedTheme))
            {
                currentTheme = storedTheme;
            }
        }
        catch
        {
            // JS noch nicht verfügbar: Default bleibt aktiv.
        }
    }

    private async Task SelectThemeAsync(string theme)
    {
        currentTheme = theme;
        await JsRuntime.InvokeVoidAsync("SKOULOUAPPTheme.set", theme);
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
                var request = new UpdateUserProfileRequest
                {
                    Vorname = ProfileModel.Vorname,
                    Nachname = ProfileModel.Nachname,
                    TelefonNummer = ProfileModel.TelefonNummer,
                    Geschlecht = ProfileModel.Geschlecht,
                    ProfilBildAssetId = ProfileModel.ProfilBildAssetId,
                    FachIds = selectedFachIds.ToList()
                };

                var result = await AuthService.UpdateProfileAsync(userId, request);
                profileSucceeded = result.Succeeded;
                profileMessage = result.Message;
                profileToastKind = result.Succeeded ? Toast.ToastKind.Success : Toast.ToastKind.Error;
                profileToastMessage = result.Message;

                if (result.Succeeded)
                {
                    fullProfile = await AuthService.GetUserProfileAsync(userId);
                }
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
                passwordToastKind = result.Succeeded ? Toast.ToastKind.Success : Toast.ToastKind.Error;
                passwordToastMessage = result.Message;
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

    private Task OpenImageLibraryAsync()
    {
        imageLibraryOpen = true;
        return Task.CompletedTask;
    }

    private Task RemoveProfileImageAsync()
    {
        ProfileModel.ProfilBildAssetId = null;
        profileImageUrl = null;
        return Task.CompletedTask;
    }

    private Task OnProfileImageSelectedAsync(BlobMediaAssetItem image)
    {
        ProfileModel.ProfilBildAssetId = image.Id;
        profileImageUrl = image.ImageUrl;
        return Task.CompletedTask;
    }

    private Task ToggleFachAsync(Guid fachId, bool isSelected)
    {
        if (isSelected)
        {
            selectedFachIds.Add(fachId);
        }
        else
        {
            selectedFachIds.Remove(fachId);
        }

        return Task.CompletedTask;
    }

    private async Task AddFachAsync()
    {
        if (isAddingFach)
        {
            return;
        }

        isAddingFach = true;
        try
        {
            var result = await AuthService.AddTeacherFachAsync(userId, newFachName);
            profileSucceeded = result.Succeeded;
            profileMessage = result.Message;
            if (!result.Succeeded)
            {
                return;
            }

            newFachName = string.Empty;
            teacherFachOptions = (await AuthService.GetTeacherFachOptionsAsync(userId)).ToList();
            if (result.FachId.HasValue)
            {
                selectedFachIds.Add(result.FachId.Value);
            }
        }
        finally
        {
            isAddingFach = false;
        }
    }

    private sealed class ProfileInput
    {
        [Required(ErrorMessage = "Veuillez saisir un prénom.")]
        public string Vorname { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez saisir un nom.")]
        public string Nachname { get; set; } = string.Empty;

        [MaxLength(50, ErrorMessage = "Le numéro de téléphone ne doit pas dépasser 50 caractères.")]
        public string? TelefonNummer { get; set; }

        public Geschlecht Geschlecht { get; set; } = Geschlecht.Divers;

        public Guid? ProfilBildAssetId { get; set; }
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
