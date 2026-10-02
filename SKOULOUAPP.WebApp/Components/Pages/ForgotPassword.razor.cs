using System.ComponentModel.DataAnnotations;
using SkoulouApp.WebApp.Services;
using Microsoft.AspNetCore.Components;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class ForgotPassword
{
    [Inject]
    private AuthService AuthService { get; set; } = default!;

    [Inject]
    private DatabaseActionGuard DatabaseActionGuard { get; set; } = default!;

    private ForgotPasswordInput? model;

    [SupplyParameterFromForm(FormName = "forgot-password-form")]
    private ForgotPasswordInput Model
    {
        get => model ??= new();
        set => model = value;
    }

    private string? message;
    private bool isSubmitting;

    private async Task HandleSubmitAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        isSubmitting = true;
        try
        {
            await DatabaseActionGuard.RunAsync(async () =>
            {
                var result = await AuthService.SendPasswordResetAsync(Model.Email);
                message = result.Message;
            });
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private sealed class ForgotPasswordInput
    {
        [Required(ErrorMessage = "Veuillez saisir une adresse e-mail.")]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }
}
