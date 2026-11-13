using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using SkoulouApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class Contact
{
    [Inject]
    private AuthService AuthService { get; set; } = default!;

    [Inject]
    private MailboxService MailboxService { get; set; } = default!;

    [Inject]
    private DatabaseActionGuard DatabaseActionGuard { get; set; } = default!;

    [Inject]
    private ResponsableKontaktService ResponsableKontakt { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

    /// <summary>Responsable produit der Schule – nur für die Direktion sichtbar.</summary>
    private ResponsableKontaktService.Responsable? responsable;

    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthStateProvider.GetAuthenticationStateAsync()).User;
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return;
        responsable = await ResponsableKontakt.GetFuerDirektionAsync(userId);
        if (string.IsNullOrWhiteSpace(Model.Email)) Model.Email = user.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
    }

    private ContactInput? model;

    [SupplyParameterFromForm(FormName = "contact-form")]
    private ContactInput Model
    {
        get => model ??= new();
        set => model = value;
    }

    private string? message;
    private bool succeeded;
    private bool isSubmitting;

    private async Task HandleSubmitAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        isSubmitting = true;
        message = null;
        try
        {
            await DatabaseActionGuard.RunAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(Model.Message) || Model.Message.Length < 10) return;
                await MailboxService.EmpfangenAsync(Model.Email.Trim(), SkoulouMarke.KontaktEmail, $"[Contact] {Model.Subject}", $"<p>Nom : {System.Net.WebUtility.HtmlEncode(Model.Name)}</p>{Model.Message}", null);
                var result = await AuthService.SendContactMessageAsync(Model.Name, Model.Email, Model.Subject, Model.Message);
                succeeded = result.Succeeded;
                message = result.Message;
                if (result.Succeeded)
                {
                    model = new ContactInput();
                }
            });
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private sealed class ContactInput
    {
        [Required(ErrorMessage = "Veuillez saisir votre nom.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez saisir une adresse e-mail.")]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez saisir un sujet.")]
        public string Subject { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez saisir un message.")]
        [MinLength(10, ErrorMessage = "Le message doit contenir au moins 10 caractères.")]
        public string Message { get; set; } = string.Empty;
    }
}
