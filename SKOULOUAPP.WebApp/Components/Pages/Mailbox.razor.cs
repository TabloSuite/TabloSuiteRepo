using System.Security.Claims;
using HalloApp.Core.DataModels.Email;
using HalloApp.Core.DataModels.Identity;
using HalloApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace HalloApp.WebApp.Components.Pages;

public partial class Mailbox
{
    [Inject] private MailboxService MailboxService { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    private Guid userId;
    private bool isAdmin;
    private bool sending;
    private Guid? ownerFilter;
    private Guid? schoolId;
    private string recipient = string.Empty;
    private string subject = string.Empty;
    private string body = string.Empty;
    private string? message;
    private IReadOnlyList<MailNachricht> mails = [];
    private IReadOnlyList<(Guid Id, string Name)> schools = [];
    private IReadOnlyList<(Guid Id, string Name)> owners = [];

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
                await ReloadAsync();
            }
        }
        finally
        {
            sending = false;
        }
    }

    private void Reply(MailNachricht mail)
    {
        recipient = mail.Absender;
        subject = mail.Betreff.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? mail.Betreff : $"Re: {mail.Betreff}";
        schoolId = mail.SchulID;
        body = string.Empty;
    }

    private async Task AssignAsync(Guid mailId, string? schoolValue)
    {
        var school = Guid.TryParse(schoolValue, out var id) ? id : (Guid?)null;
        if (await MailboxService.ZuordnenAsync(userId, mailId, school)) await ReloadAsync();
    }
}
