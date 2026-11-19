using System.ComponentModel.DataAnnotations;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;

namespace ENote.WebApp.Components.Pages;

public partial class Register(AuthService authService, NavigationManager navigationManager, IHttpContextAccessor httpZugriff)
{
    // Statisches Formular (siehe Register.razor): Feldnamen « Model.… » kommen aus den Bindungen an Model.
    [SupplyParameterFromForm(FormName = "register-form")]
    private RegisterInput? Model { get; set; }

    private string? errorMessage;
    private IReadOnlyList<string> laender = [];

    protected override async Task OnInitializedAsync()
    {
        Model ??= new RegisterInput();
        laender = await authService.GetLaenderAsync();
        if (!laender.Contains(Model.Land)) Model.Land = laender.FirstOrDefault() ?? ENote.WebApp.Services.LaenderAuswahl.Standard;
    }

    private async Task HandleSubmit()
    {
        errorMessage = null;
        var m = Model!;
        if (!m.CguAcceptees)
        {
            errorMessage = "Veuillez accepter les Conditions Générales d'Utilisation.";
            return;
        }

        var (succeeded, error) = await authService.RegisterBesitzerAsync(
            m.Vorname,
            m.Nachname,
            m.Pseudonym,
            m.Email,
            m.Passwort,
            m.SalonName,
            m.SalonStandort,
            m.Slogan,
            land: m.Land,
            anschluss: Services.Sicherheit.Anschluss.Adresse(httpZugriff.HttpContext),
            telefon: m.Telefon);

        if (succeeded)
        {
            // Testabend 18.11.2026: ohne vollständiges Neuladen zeigte der interaktive Router « Not found »
            // (die Anmeldeseite ist rein statisch). Hier antwortet der Server ohnehin mit einer Weiterleitung.
            navigationManager.NavigateTo("/auth/login?info=registered", forceLoad: true);
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
