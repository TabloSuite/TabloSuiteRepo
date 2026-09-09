using System.ComponentModel.DataAnnotations;
using HalloApp.WebApp.Services;
using Microsoft.AspNetCore.Components;

namespace HalloApp.WebApp.Components.Pages;

public partial class ResetPassword
{
    [Inject]
    private AuthService AuthService { get; set; } = default!;

    [Inject]
    private DatabaseActionGuard DatabaseActionGuard { get; set; } = default!;

    [SupplyParameterFromQuery]
    private string? Email { get; set; }

    [SupplyParameterFromQuery]
    private string? Token { get; set; }

    private ResetPasswordInput? model;

    [SupplyParameterFromForm(FormName = "reset-password-form")]
    private ResetPasswordInput Model
    {
        get => model ??= new();
        set => model = value;
    }

    private string? message;
    private bool succeeded;
    private bool isSubmitting;

    protected override void OnInitialized()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Token))
        {
            message = "Le lien de réinitialisation est invalide ou incomplet.";
        }
    }

    private async Task HandleSubmitAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Token))
        {
            message = "Le lien de réinitialisation est invalide ou incomplet.";
            return;
        }

        isSubmitting = true;
        try
        {
            await DatabaseActionGuard.RunAsync(async () =>
            {
                var result = await AuthService.ResetPasswordAsync(Email!, Token!, Model.Password);
                succeeded = result.Succeeded;
                message = result.Message;
            });
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private sealed class ResetPasswordInput
    {
        [Required(ErrorMessage = "Veuillez saisir un mot de passe.")]
        [DataType(DataType.Password)]
        [MinLength(6, ErrorMessage = "Le mot de passe doit contenir au moins 6 caractères.")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez confirmer le mot de passe.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Les mots de passe ne correspondent pas.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
