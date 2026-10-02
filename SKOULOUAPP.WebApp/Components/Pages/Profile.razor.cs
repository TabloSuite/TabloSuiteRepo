using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using SkoulouApp.Core.Services.Storage;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.Models.Profile;
using SkoulouApp.Core.Models.TeacherLessons;
using SkoulouApp.WebApp.Components.Shared;
using SkoulouApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace SkoulouApp.WebApp.Components.Pages;

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

    [SupplyParameterFromQuery(Name = "returnUrl")]
    private string? ReturnUrl { get; set; }

    private string ReturnTarget => ValidReturnTarget(ReturnUrl);
    private bool HasReturnTarget => ReturnTarget != "/";

    private static string ValidReturnTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || !target.StartsWith('/') || target.StartsWith("//")
            || target.Contains('\\') || target.Any(char.IsControl)
            || Uri.TryCreate(target, UriKind.Absolute, out _)) return "/";

        var path = target.Split('?', '#')[0].TrimEnd('/');
        return path.Equals("/profil", StringComparison.OrdinalIgnoreCase) ? "/" : target;
    }

    [Inject]
    private IJSRuntime JsRuntime { get; set; } = default!;

    [Inject]
    private IBlobMediaLibraryService MediaLibrary { get; set; } = default!;

    private Guid userId;
    private string? email;
    private bool isLoading = true;

    private UserProfileModel? fullProfile;
    private bool isTeacher;

    private List<FachOptionItem> teacherFachOptions = [];
    private HashSet<Guid> selectedFachIds = [];
    private string newFachName = string.Empty;
    private bool isAddingFach;
    private IBrowserFile? selectedProfileImage;
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
                if (selectedProfileImage is not null)
                {
                    var schoolId = fullProfile?.Schulen.FirstOrDefault()?.SchoolId;
                    try
                    {
                        await using var imageStream = selectedProfileImage.OpenReadStream(5 * 1024 * 1024);
                        var uploaded = await MediaLibrary.UploadProfileImageAsync(userId, schoolId, imageStream, selectedProfileImage.Name, selectedProfileImage.ContentType);
                        ProfileModel.ProfilBildAssetId = uploaded.Id;
                    }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
                    {
                        profileToastKind = Toast.ToastKind.Error;
                        profileToastMessage = ex is IOException ? "La photo dépasse la taille maximale de 5 Mo." : ex.Message;
                        return;
                    }
                }

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
                    profileImageUrl = fullProfile?.ProfilBildUrl;
                    selectedProfileImage = null;
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

    private void SelectProfileImage(InputFileChangeEventArgs args) => selectedProfileImage = args.File;

    private Task RemoveProfileImageAsync()
    {
        selectedProfileImage = null;
        ProfileModel.ProfilBildAssetId = null;
        profileImageUrl = null;
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
