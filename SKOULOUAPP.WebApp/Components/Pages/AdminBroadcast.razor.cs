using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services;
using SkoulouApp.Core.Services.Email;
using SkoulouApp.Core.Services.Text;
using SkoulouApp.WebApp.Components.Shared;
using SkoulouApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class AdminBroadcast
{
    [Parameter] public bool ShowAnnouncements { get; set; } = true;
    [Parameter] public bool ShowEmailComposer { get; set; } = true;
    [Parameter] public EventCallback EmailSent { get; set; }

    [Inject]
    private MailboxService MailboxService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private HomePageService HomePageService { get; set; } = default!;

    [Inject]
    private AnkuendigungService AnkuendigungService { get; set; } = default!;

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
    private bool isSavingAnnouncement;
    private string? announcementMessage;
    private List<Ankuendigung> announcements = [];

    // Ankündigungen serverseitig: Suche im Titel (verzögert), „Afficher plus“.
    private const int AnkuendigungenSeite = 50;
    private bool announcementsMehr;
    private string announcementSuche = string.Empty;
    private readonly SuchVerzoegerung announcementSuchVerzoegerung = new();

    private async Task AnkuendigungenLadenAsync()
        => (announcements, announcementsMehr) = await AnkuendigungService.SucheAsync(announcementSuche, 0, Math.Max(AnkuendigungenSeite, announcements.Count), ComponentToken);

    private Task AnkuendigungenSuchenAsync() => announcementSuchVerzoegerung.AusfuehrenAsync(async () =>
    {
        try { (announcements, announcementsMehr) = await AnkuendigungService.SucheAsync(announcementSuche, 0, AnkuendigungenSeite, ComponentToken); }
        catch (Exception ex) when (IstAbbruch(ex)) { }
    });

    private async Task MehrAnkuendigungenAsync()
    {
        var (neu, mehr) = await AnkuendigungService.SucheAsync(announcementSuche, announcements.Count, AnkuendigungenSeite, ComponentToken);
        announcements.AddRange(neu);
        announcementsMehr = mehr;
    }

    public override void Dispose()
    {
        announcementSuchVerzoegerung.Dispose();
        base.Dispose();
    }
    private AnnouncementInput announcement = new();

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
            if (ShowAnnouncements) await AnkuendigungenLadenAsync();
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

    private async Task PublishAnnouncementAsync()
    {
        if (isSavingAnnouncement) return;
        isSavingAnnouncement = true;
        try
        {
            await DatabaseActionGuard.RunAsync(async () =>
            {
                var expiryUtc = announcement.ExpiresAtLocal.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(announcement.ExpiresAtLocal, DateTimeKind.Local).ToUniversalTime()
                    : announcement.ExpiresAtLocal.ToUniversalTime();
                var result = await AnkuendigungService.ErstellenAsync(announcement.Title, announcement.Body, expiryUtc, DateTime.UtcNow, ComponentToken);
                announcementMessage = result.Message;
                if (result.Succeeded)
                {
                    announcement = new();
                    await AnkuendigungenLadenAsync();
                }
            });
        }
        finally { isSavingAnnouncement = false; }
    }

    private async Task DeleteAnnouncementAsync(Guid id)
    {
        if (isSavingAnnouncement) return;
        isSavingAnnouncement = true;
        try
        {
            await DatabaseActionGuard.RunAsync(async () =>
            {
                if (await AnkuendigungService.LoeschenAsync(id, ComponentToken))
                {
                    announcementMessage = "Annonce supprimée.";
                    await AnkuendigungenLadenAsync();
                }
            });
        }
        finally { isSavingAnnouncement = false; }
    }

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

                var auth = await AuthenticationStateProvider.GetAuthenticationStateAsync();
                if (!Guid.TryParse(auth.User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
                {
                    succeeded = false;
                    message = "Accès refusé.";
                    return;
                }

                var result = await MailboxService.SendenAnGruppeAsync(adminId,
                    BuildCriteria(),
                    new EmailMessage
                    {
                        Subject = Model.Subject,
                        HtmlBody = html,
                        TextBody = plainBody
                    }, ComponentToken);

                succeeded = result.FailedCount == 0 && result.SentCount > 0;
                message = result.SentCount > 0
                    ? $"{result.SentCount} e-mail(s) envoyé(s)." + (result.FailedCount > 0 ? $" {result.FailedCount} échec(s)." : string.Empty)
                    : "Aucun e-mail n'a été envoyé. " + string.Join(" ", result.Errors);
                if (result.SentCount > 0) await EmailSent.InvokeAsync();
            });
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private sealed class AnnouncementInput
    {
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public DateTime ExpiresAtLocal { get; set; } = DateTime.Now.AddDays(7);
    }

    private sealed class BroadcastInput
    {
        public BroadcastAudience Audience { get; set; } = BroadcastAudience.ManualAddresses;

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
