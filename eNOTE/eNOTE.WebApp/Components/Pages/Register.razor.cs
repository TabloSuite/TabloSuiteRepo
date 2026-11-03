using System.ComponentModel.DataAnnotations;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;

namespace ENote.WebApp.Components.Pages;

public partial class Register(AuthService authService, NavigationManager navigationManager)
{
    [SupplyParameterFromForm(FormName = "register-form")]
    private RegisterInput? Model { get; set; }

    private RegisterInput FormModel => Model ??= new RegisterInput();

    private string? errorMessage;
    private bool isSubmitting;

    private async Task HandleSubmit()
    {
        isSubmitting = true;
        errorMessage = null;

        var (succeeded, error) = await authService.RegisterBesitzerAsync(
            FormModel.Vorname,
            FormModel.Nachname,
            FormModel.Email,
            FormModel.Passwort,
            FormModel.SalonName,
            FormModel.SalonStandort,
            FormModel.Slogan);

        isSubmitting = false;

        if (succeeded)
        {
            navigationManager.NavigateTo("/auth/login?info=registered");
            return;
        }

        errorMessage = error;
    }

    private sealed class RegisterInput
    {
        [Required(ErrorMessage = "Veuillez saisir votre prénom.")]
        public string Vorname { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez saisir votre nom.")]
        public string Nachname { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez saisir une adresse e-mail.")]
        [EmailAddress(ErrorMessage = "Adresse e-mail invalide.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez saisir un mot de passe.")]
        [MinLength(6, ErrorMessage = "Au moins 6 caractères.")]
        public string Passwort { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez indiquer le nom de votre salon.")]
        public string SalonName { get; set; } = string.Empty;

        public string? SalonStandort { get; set; }

        public string? Slogan { get; set; }
    }
}
