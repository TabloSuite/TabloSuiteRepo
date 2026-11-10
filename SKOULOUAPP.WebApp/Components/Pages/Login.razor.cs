using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;

namespace SkoulouApp.WebApp.Components.Pages;

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
        errorMessage = Error switch
        {
            "account-blocked" => "Votre compte n'est pas encore validé ou a été bloqué. Contactez votre établissement pour en savoir plus.",
            "invalid-credentials" => "Identifiant ou mot de passe incorrect.",
            _ => null
        };

        infoMessage = Info == "password-updated"
            ? "Votre mot de passe a été modifié. Veuillez vous reconnecter."
            : null;
    }

    private sealed class LoginInput
    {
        [Required(ErrorMessage = "Veuillez saisir votre identifiant.")]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez saisir un mot de passe.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}

