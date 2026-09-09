using System.ComponentModel.DataAnnotations;
using HalloApp.Core.DataModels.Schools;
using HalloApp.Core.Services;
using HalloApp.Core.Services.Email;
using HalloApp.Core.Services.Text;
using HalloApp.WebApp.Components.Shared;
using HalloApp.WebApp.Services;
using Microsoft.AspNetCore.Components;

namespace HalloApp.WebApp.Components.Pages;

public partial class AdminBroadcast
{
    [Inject]
    private BroadcastEmailService BroadcastEmailService { get; set; } = default!;

    [Inject]
    private HomePageService HomePageService { get; set; } = default!;

    [Inject]
    private DatabaseActionGuard DatabaseActionGuard { get; set; } = default!;

    private IReadOnlyList<School> schools = [];

    private BroadcastInput? model;

    [SupplyParameterFromForm(FormName = "broadcast-form")]
    private BroadcastInput Model
    {
        get => model ??= new();
        set => model = value;
    }

    private string? message;
    private bool succeeded;
    private bool isSubmitting;

    private IReadOnlyList<ModernDropdownOption<BroadcastAudience?>> AudienceOptions =>
    [
        new(BroadcastAudience.Students, "Élèves"),
        new(BroadcastAudience.Teachers, "Enseignants"),
        new(BroadcastAudience.SchoolLeaders, "Directeurs"),
        new(BroadcastAudience.Everyone, "Tout le monde"),
        new(BroadcastAudience.ManualAddresses, "Adresses saisies uniquement")
    ];

    private IReadOnlyList<ModernDropdownOption<string?>> SchoolOptions =>
    [
        new(string.Empty, "Toutes les écoles"),
        .. schools.Select(school => new ModernDropdownOption<string?>(school.Id.ToString(), school.Name, $"{school.City}, {school.Country}"))
    ];

    private Task SetAudienceAsync(BroadcastAudience? audience)
    {
        if (audience is BroadcastAudience value)
        {
            Model.Audience = value;
        }

        return Task.CompletedTask;
    }

    private Task SetSchoolAsync(string? schoolId)
    {
        Model.SchoolId = schoolId;
        return Task.CompletedTask;
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            schools = await HomePageService.GetSchoolsAsync(ComponentToken);
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private BroadcastCriteria BuildCriteria() => new()
    {
        Audience = Model.Audience,
        SchoolId = Guid.TryParse(Model.SchoolId, out var id) ? id : null,
        ZusaetzlicheAdressen = ParseAdressen(Model.ManualRecipients)
    };

    /// <summary>
    /// Zerlegt die Freitexteingabe in einzelne Adressen. Erlaubt sind Komma,
    /// Semikolon, Leerzeichen und Zeilenumbrüche als Trennzeichen.
    /// </summary>
    private static List<string> ParseAdressen(string? eingabe)
        => string.IsNullOrWhiteSpace(eingabe)
            ? []
            : eingabe
                .Split([',', ';', '\n', '\r', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

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
                var sanitizedBody = HtmlSanitizer.Sanitize(Model.Body);
                var plainBody = HtmlSanitizer.ToPlainText(sanitizedBody);
                if (plainBody.Length < 5)
                {
                    succeeded = false;
                    message = "Le message est trop court.";
                    return;
                }

                var html = EmailTemplates.Wrap(
                    Model.Subject,
                    $"<div>{sanitizedBody}</div>");

                var result = await BroadcastEmailService.SendBroadcastAsync(
                    BuildCriteria(),
                    new EmailMessage
                    {
                        Subject = Model.Subject,
                        HtmlBody = html,
                        TextBody = plainBody
                    });

                succeeded = result.FailedCount == 0 && result.SentCount > 0;
                message = result.SentCount > 0
                    ? $"{result.SentCount} e-mail(s) envoyé(s)." + (result.FailedCount > 0 ? $" {result.FailedCount} échec(s)." : string.Empty)
                    : "Aucun e-mail n'a été envoyé. " + string.Join(" ", result.Errors);
            });
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private sealed class BroadcastInput
    {
        public BroadcastAudience Audience { get; set; } = BroadcastAudience.Students;

        public string? SchoolId { get; set; }

        /// <summary>Frei eingegebene Adressen, getrennt durch Komma, Semikolon oder Zeilenumbruch.</summary>
        public string? ManualRecipients { get; set; }

        [Required(ErrorMessage = "Veuillez saisir un sujet.")]
        public string Subject { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez saisir un message.")]
        [MinLength(5, ErrorMessage = "Le message est trop court.")]
        public string Body { get; set; } = string.Empty;
    }
}
