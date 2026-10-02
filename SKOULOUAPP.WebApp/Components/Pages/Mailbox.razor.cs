using System.Security.Claims;
using SkoulouApp.Core.DataModels.Email;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.Services.Text;
using SkoulouApp.WebApp.Components.Shared;
using SkoulouApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class Mailbox
{
    [Inject] private MailboxService MailboxService { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    private Guid userId;
    private bool isAdmin;
    private bool sending;
    private bool composeOpen;
    private bool broadcastMode;
    private bool showSent;
    private string search = string.Empty;
    private Guid? ownerFilter;
    private Guid? schoolId;
    private Guid? selectedRecipientId;
    private string recipientRole = "all";
    private string recipient = string.Empty;
    private string subject = string.Empty;
    private string body = string.Empty;
    private string? message;
    private IReadOnlyList<MailNachricht> mails = [];
    private IReadOnlyList<(Guid Id, string Name)> schools = [];
    private IReadOnlyList<(Guid Id, string Name)> owners = [];
    private IReadOnlyList<MailEmpfaenger> schoolRecipients = [];
    private IReadOnlyList<ModernDropdownOption<string?>> RecipientRoleOptions =>
    [
        new("all", "Tous les destinataires"),
        new("direction", "Direction"),
        new("teachers", "Enseignants"),
        new("students", "Élèves")
    ];
    private IReadOnlyList<ModernDropdownOption<Guid?>> RecipientOptions =>
        schoolRecipients.Where(contact => recipientRole switch
        {
            "direction" => contact.IstDirektion,
            "teachers" => contact.IstLehrer,
            "students" => contact.IstSchueler,
            _ => true
        }).Select(contact => new ModernDropdownOption<Guid?>(contact.Id,
            string.IsNullOrWhiteSpace(contact.Name) ? contact.Email : contact.Name, contact.Email)).ToList();
    private IReadOnlyList<ModernDropdownOption<Guid?>> OwnerOptions =>
    [
        new(null, "Tous les messages"),
        .. owners.Select(owner => new ModernDropdownOption<Guid?>(owner.Id, owner.Name))
    ];
    private IReadOnlyList<ModernDropdownOption<Guid?>> SchoolOptions =>
    [
        new(null, isAdmin ? "Sans école (administration)" : "Choisir une école"),
        .. schools.Select(school => new ModernDropdownOption<Guid?>(school.Id, school.Name))
    ];
    private IReadOnlyList<ModernDropdownOption<Guid?>> AssignmentOptions =>
    [
        new(null, "Administration uniquement"),
        .. schools.Select(school => new ModernDropdownOption<Guid?>(school.Id, school.Name))
    ];
    private IReadOnlyList<MailNachricht> VisibleMails => mails
        .Where(mail => mail.IstEingehend != showSent && (string.IsNullOrWhiteSpace(search)
            || mail.Betreff.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
            || mail.Absender.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
            || mail.Empfaenger.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
            || HtmlSanitizer.ToPlainText(mail.Inhalt).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)))
        .ToList();

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        if (!Guid.TryParse(state.User.FindFirstValue(ClaimTypes.NameIdentifier), out userId)) return;
        isAdmin = state.User.IsInRole(Rollen.SuperAdmin);
        schools = await MailboxService.SchulenAsync(userId, isAdmin);
        if (isAdmin) owners = await MailboxService.ProduktOwnerAsync(userId);
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (userId != Guid.Empty) mails = await MailboxService.LadeAsync(userId, isAdmin, ownerFilter);
    }

    private async Task SetOwnerFilterAsync(Guid? id)
    {
        ownerFilter = id;
        await ReloadAsync();
    }

    private async Task SetSchoolAsync(Guid? id)
    {
        schoolId = id;
        selectedRecipientId = null;
        recipient = string.Empty;
        recipientRole = "all";
        schoolRecipients = id.HasValue ? await MailboxService.EmpfaengerAsync(userId, id.Value) : [];
    }

    private Task SetRecipientRoleAsync(string? role)
    {
        recipientRole = role ?? "all";
        selectedRecipientId = null;
        return Task.CompletedTask;
    }

    private Task SetRecipientAsync(Guid? id)
    {
        selectedRecipientId = id;
        if (id is Guid recipientId)
            recipient = schoolRecipients.FirstOrDefault(contact => contact.Id == recipientId)?.Email ?? string.Empty;
        return Task.CompletedTask;
    }

    private Task SetAssignmentAsync(Guid mailId, Guid? id) => AssignAsync(mailId, id);

    private async Task SendAsync()
    {
        if (sending || userId == Guid.Empty) return;
        sending = true;
        try
        {
            var result = await MailboxService.SendenAsync(userId, isAdmin, schoolId, recipient, subject, body);
            message = result.Message;
            if (result.Succeeded)
            {
                recipient = subject = body = string.Empty;
                composeOpen = false;
                showSent = true;
                await ReloadAsync();
            }
        }
        finally
        {
            sending = false;
        }
    }

    private async Task Reply(MailNachricht mail)
    {
        broadcastMode = false;
        await SetSchoolAsync(mail.SchulID);
        recipient = mail.Absender;
        subject = mail.Betreff.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? mail.Betreff : $"Re: {mail.Betreff}";
        body = string.Empty;
        composeOpen = true;
    }

    private async Task OnBroadcastSentAsync()
    {
        showSent = true;
        await ReloadAsync();
    }

    private async Task AssignAsync(Guid mailId, Guid? school)
    {
        if (await MailboxService.ZuordnenAsync(userId, mailId, school)) await ReloadAsync();
    }
}
