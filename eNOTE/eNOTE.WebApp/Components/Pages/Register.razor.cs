using System.ComponentModel.DataAnnotations;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;

namespace ENote.WebApp.Components.Pages;

public partial class Register(AuthService authService, NavigationManager navigationManager, IHttpContextAccessor httpZugriff)
{
    [SupplyParameterFromForm(FormName = "register-form")]
    private RegisterInput? Model { get; set; }

    private RegisterInput FormModel => Model ??= new RegisterInput();

    private string? errorMessage;
    private bool isSubmitting;
    private bool cguOffen;
    private IReadOnlyList<Shared.EnoteSelectOption<string>> landOptionen = [];

    private string? anschluss;

    protected override async Task OnInitializedAsync()
    {
        anschluss = Services.Sicherheit.Anschluss.Adresse(httpZugriff.HttpContext);
        landOptionen = (await authService.GetLaenderAsync())
            .Select(l => new Shared.EnoteSelectOption<string> { Value = l, Label = l }).ToList();
    }

    private async Task HandleSubmit()
    {
        isSubmitting = true;
        errorMessage = null;

        if (!FormModel.CguAcceptees)
        {
            isSubmitting = false;
            errorMessage = "Veuillez accepter les Conditions Générales d'Utilisation.";
            return;
        }

        var (succeeded, error) = await authService.RegisterBesitzerAsync(
            FormModel.Vorname,
            FormModel.Nachname,
            FormModel.Pseudonym,
            FormModel.Email,
            FormModel.Passwort,
            FormModel.SalonName,
            FormModel.SalonStandort,
            FormModel.Slogan,
            land: FormModel.Land,
            anschluss: anschluss ?? Services.Sicherheit.Anschluss.Adresse(httpZugriff.HttpContext),
            telefon: FormModel.Telefon);

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

        [Required(ErrorMessage = "Veuillez choisir un pseudonyme.")]
        public string Pseudonym { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez saisir une adresse e-mail.")]
        [EmailAddress(ErrorMessage = "Adresse e-mail invalide.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez saisir votre numéro de téléphone.")]
        public string Telefon { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez saisir un mot de passe.")]
        [MinLength(6, ErrorMessage = "Au moins 6 caractères.")]
        public string Passwort { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez indiquer le nom de votre établissement.")]
        public string SalonName { get; set; } = string.Empty;

        public string? SalonStandort { get; set; }

        /// <summary>Ortszeit und Mobile-Money-Anbieter des Ladens; Standard Togo (Hauptmarkt).</summary>
        public string Land { get; set; } = ENote.WebApp.Services.LaenderAuswahl.Standard;

        public string? Slogan { get; set; }

        [System.ComponentModel.DataAnnotations.Range(typeof(bool), "true", "true", ErrorMessage = "Veuillez accepter les CGU.")]
        public bool CguAcceptees { get; set; }
    }
}
