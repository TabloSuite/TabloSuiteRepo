using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;

namespace HalloApp.WebApp.Components.Pages;

public partial class Login
{
    private LoginInput? model;

    [SupplyParameterFromForm(FormName = "login-form")]
    private LoginInput Model
    {
        get => model ??= new();
        set => model = value;
    }

    private string? errorMessage;
    private string? infoMessage;

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    [SupplyParameterFromQuery(Name = "error")]
    private string? Error { get; set; }

    [SupplyParameterFromQuery(Name = "info")]
    private string? Info { get; set; }

    protected override void OnParametersSet()
    {
        errorMessage = Error == "invalid-credentials"
            ? "Adresse e-mail ou mot de passe incorrect."
            : null;

        infoMessage = Info == "password-updated"
            ? "Ton mot de passe a été modifié. Connecte-toi à nouveau."
            : null;
    }

    private sealed class LoginInput
    {
        [Required(ErrorMessage = "Veuillez saisir une adresse e-mail.")]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez saisir un mot de passe.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}

