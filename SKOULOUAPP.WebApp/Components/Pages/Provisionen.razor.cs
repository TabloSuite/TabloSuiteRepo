using HalloApp.Core.Models.Billing;
using HalloApp.Core.Models.Dashboard;
using HalloApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace HalloApp.WebApp.Components.Pages;

public partial class Provisionen
{
    [Inject] private DashboardService DashboardService { get; set; } = default!;
    [Inject] private AbrechnungService AbrechnungService { get; set; } = default!;
    [Inject] private ProvisionsService ProvisionsService { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    private bool isManager;
    private bool isSuperAdmin;
    private bool isProduktOwner;
    private IReadOnlyList<AdminSchoolOverview> schools = [];
    private Guid? selectedSchoolId;
    private ProvisionsUebersicht? offeneUebersicht;
    private IReadOnlyList<ProvisionsUebersicht> periodenUebersichten = [];
    private bool isLehrerView;
    private IReadOnlyList<ProvisionsUebersicht> lehrerUebersichten = [];
    private IReadOnlyList<ProvisionsUebersicht> produktOwnerUebersichten = [];
    private bool isLoading = true;
    private string activeManagerTab = "open";

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;

            if (user.IsInRole("ProduktOwner") && TryGetUserId(user, out var produktOwnerId))
            {
                isProduktOwner = true;
                produktOwnerUebersichten = await ProvisionsService.GetProduktOwnerUebersichtenAsync(produktOwnerId, ComponentToken);
            }
            else if (user.IsInRole("SuperAdmin"))
            {
                isManager = true;
                isSuperAdmin = true;
                schools = await DashboardService.GetAdminSchoolsAsync(ComponentToken);
            }
            else if (user.IsInRole("SchulLeiter") || user.IsInRole("Koordinator"))
            {
                isManager = true;
                if (TryGetUserId(user, out var userId))
                {
                    schools = (await DashboardService.GetTeacherSchoolsAsync(userId, ComponentToken))
                        .Select(x => new AdminSchoolOverview
                        {
                            SchoolId = x.SchoolId,
                            Name = x.SchoolName,
                            City = x.City,
                            StudentCount = x.StudentCount
                        })
                        .ToList();
                }
            }
            else if (user.IsInRole("Lehrer") && TryGetUserId(user, out var lehrerId))
            {
                isLehrerView = true;
                lehrerUebersichten = await ProvisionsService.GetLehrerUebersichtenAsync(lehrerId, ComponentToken);
            }

            if (isManager && schools.Count == 1)
            {
                selectedSchoolId = schools[0].SchoolId;
                await LoadSchoolAsync();
            }
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task SelectSchoolAsync(Guid schoolId)
    {
        selectedSchoolId = schoolId;
        await LoadSchoolAsync();
    }

    private async Task LoadSchoolAsync()
    {
        if (selectedSchoolId is null)
        {
            offeneUebersicht = null;
            periodenUebersichten = [];
            return;
        }

        offeneUebersicht = await ProvisionsService.GetOffeneUebersichtAsync(selectedSchoolId.Value, ComponentToken);
        var historie = await AbrechnungService.GetHistorieAsync(selectedSchoolId.Value, ComponentToken);
        var uebersichten = new List<ProvisionsUebersicht>();
        foreach (var periode in historie.Where(x => x.IstAbgeschlossen))
        {
            uebersichten.Add(await ProvisionsService.GetPeriodeUebersichtAsync(periode.Id, ComponentToken));
        }

        periodenUebersichten = uebersichten;
    }

    private decimal GetVisibleTotal(ProvisionsUebersicht uebersicht)
        => isSuperAdmin
            ? uebersicht.Gesamtumsatz
            : uebersicht.ProvisionSchulleiter + uebersicht.ProvisionLehrer;

    private Task SetManagerTabAsync(string tab)
    {
        activeManagerTab = tab;
        return Task.CompletedTask;
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var value = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out userId);
    }
}
