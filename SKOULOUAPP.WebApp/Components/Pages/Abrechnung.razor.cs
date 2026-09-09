using HalloApp.Core.DataModels.Schools;
using HalloApp.Core.Models.Billing;
using HalloApp.Core.Models.Dashboard;
using HalloApp.Core.Services;
using HalloApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace HalloApp.WebApp.Components.Pages;

public partial class Abrechnung
{
    [Inject]
    private DashboardService DashboardService { get; set; } = default!;

    [Inject]
    private AbrechnungService AbrechnungService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    private IReadOnlyList<AdminSchoolOverview> schools = [];
    private AbrechnungsUebersicht? uebersicht;
    private IReadOnlyList<Abrechnungsperiode> historie = [];
    private IReadOnlyList<Abrechnungsperiode> automatischeOffenePerioden = [];
    private IReadOnlyList<GeschlosseneKlasseOffeneAboForderung> offeneForderungenAusGeschlossenenKlassen = [];
    private IReadOnlyList<AktiveKlasseUeberfaelligeAboForderung> ueberfaelligeForderungenAusAktivenKlassen = [];
    private IReadOnlyList<OffeneSuperAdminBestaetigung> offeneSuperAdminBestaetigungen = [];

    private Guid? selectedSchoolId;
    private string schoolSearchTerm = string.Empty;

    private IReadOnlyList<AdminSchoolOverview> GefilterteSchulen => string.IsNullOrWhiteSpace(schoolSearchTerm)
        ? schools
        : schools.Where(school => school.Name.Contains(schoolSearchTerm.Trim(), StringComparison.CurrentCultureIgnoreCase)
            || school.City.Contains(schoolSearchTerm.Trim(), StringComparison.CurrentCultureIgnoreCase)
            || school.Country.Contains(schoolSearchTerm.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .ToList();

    /// <summary>SuperAdmin: darf erstellen und bestätigen. Sonst SchulLeiter (akzeptieren/ablehnen).</summary>
    private bool isSuperAdmin;
    private bool isProduktOwner;

    private bool isReadOnly => !isSuperAdmin;
    private bool CanManageBilling => isSuperAdmin || isProduktOwner;

    private IReadOnlyList<Abrechnungsperiode> PendingDirectorApprovals =>
        isSuperAdmin
            ? []
            : historie.Where(periode => periode.Status == AbrechnungStatus.Erstellt).ToList();

    private IReadOnlyList<Abrechnungsperiode> PendingSuperAdminConfirmations =>
        isSuperAdmin
            ? historie.Where(periode => periode.Status == AbrechnungStatus.Akzeptiert).ToList()
            : [];

    private decimal OffeneSuperAdminBestaetigungenSumme => offeneSuperAdminBestaetigungen.Sum(periode => periode.Umsatz);

    // Bestätigungsdialoge / Aktionen.
    private bool confirmCreateOpen;
    private Guid? confirmPeriodId;
    private Guid? rejectPeriodId;
    private string rejectReason = string.Empty;

    // Zeitraumende für Vorschau und Rechnung. Beginn ist stets das Ende der letzten Rechnung.
    private DateTime? filterBisLokal;

    private string? message;
    private string? errorMessage;
    private bool isDatabaseActionInProgress;
    private Guid? currentUserId;

    [Inject]
    private DatabaseActionGuard DatabaseActionGuard { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        var token = ComponentToken;
        try
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;

            if (user.IsInRole("SuperAdmin"))
            {
                isSuperAdmin = true;
                var superAdminIdValue = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(superAdminIdValue, out var superAdminId))
                {
                    schools = [];
                    return;
                }

                currentUserId = superAdminId;
                schools = await DashboardService.GetAdminSchoolsAsync(token);
                offeneSuperAdminBestaetigungen = await AbrechnungService.GetOffeneSuperAdminBestaetigungenAsync(token);
                offeneForderungenAusGeschlossenenKlassen = await AbrechnungService.GetOffeneForderungenAusGeschlossenenKlassenAsync(token);
                ueberfaelligeForderungenAusAktivenKlassen = await AbrechnungService.GetUeberfaelligeForderungenAusAktivenKlassenAsync(cancellationToken: token);
                return;
            }

            if (user.IsInRole("ProduktOwner"))
            {
                isProduktOwner = true;
                var produktOwnerIdValue = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(produktOwnerIdValue, out var produktOwnerId))
                {
                    schools = [];
                    return;
                }

                currentUserId = produktOwnerId;
                schools = (await DashboardService.GetSchoolLeaderSchoolsAsync(produktOwnerId, token))
                    .Select(school => new AdminSchoolOverview
                    {
                        SchoolId = school.SchoolId,
                        Name = school.SchoolName,
                        City = school.City,
                        StudentCount = school.StudentCount
                    })
                    .ToList();
                if (schools.Count == 1)
                {
                    selectedSchoolId = schools[0].SchoolId;
                    await LoadSchoolAsync();
                }

                return;
            }

            // Der Direktor (SchulLeiter) darf nicht erstellen, aber akzeptieren/ablehnen.
            isSuperAdmin = false;

            var directorUserIdValue = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(directorUserIdValue, out var userId))
            {
                schools = [];
                return;
            }
            currentUserId = userId;

            var teacherSchools = await DashboardService.GetTeacherSchoolsAsync(userId, token);
            schools = teacherSchools
                .Select(x => new AdminSchoolOverview
                {
                    SchoolId = x.SchoolId,
                    Name = x.SchoolName,
                    City = x.City,
                    StudentCount = x.StudentCount
                })
                .ToList();

            // Bei genau einer Schule automatisch auswählen.
            if (schools.Count == 1)
            {
                selectedSchoolId = schools[0].SchoolId;
                await LoadSchoolAsync();
            }
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private async Task SelectSchoolAsync(Guid schoolId)
    {
        if (isProduktOwner && !schools.Any(school => school.SchoolId == schoolId))
        {
            return;
        }

        selectedSchoolId = schoolId;
        filterBisLokal = null;
        await LoadSchoolAsync();
    }

    private async Task LoadSchoolAsync()
    {
        if (selectedSchoolId is null)
        {
            uebersicht = null;
            historie = [];
            automatischeOffenePerioden = [];
            return;
        }

        if (isProduktOwner && !schools.Any(school => school.SchoolId == selectedSchoolId.Value))
        {
            selectedSchoolId = null;
            uebersicht = null;
            historie = [];
            automatischeOffenePerioden = [];
            return;
        }

        var bisUtcFilter = filterBisLokal?.ToUniversalTime();
        uebersicht = await AbrechnungService.GetOffeneUebersichtAsync(selectedSchoolId.Value, bisUtcFilter);
        historie = await AbrechnungService.GetHistorieAsync(selectedSchoolId.Value);
        automatischeOffenePerioden = isSuperAdmin
            ? historie
                .Where(periode => periode.AutomatischDurchKlassenabschlussErstellt
                    && periode.Status != AbrechnungStatus.Bestaetigt)
                .ToList()
            : [];
        if (isSuperAdmin)
        {
            offeneSuperAdminBestaetigungen = await AbrechnungService.GetOffeneSuperAdminBestaetigungenAsync(ComponentToken);
            offeneForderungenAusGeschlossenenKlassen = await AbrechnungService.GetOffeneForderungenAusGeschlossenenKlassenAsync(ComponentToken);
            ueberfaelligeForderungenAusAktivenKlassen = await AbrechnungService.GetUeberfaelligeForderungenAusAktivenKlassenAsync(cancellationToken: ComponentToken);
        }
    }

    private async Task ApplyFilterAsync() => await LoadSchoolAsync();

    private async Task ResetFilterAsync()
    {
        filterBisLokal = null;
        await LoadSchoolAsync();
    }

    // --- SuperAdmin: Abrechnung erstellen ---

    private Task ConfirmCreateAsync() => RunDatabaseActionAsync(ConfirmCreateCoreAsync);

    private async Task ConfirmCreateCoreAsync()
    {
        confirmCreateOpen = false;
        if (!CanManageBilling || selectedSchoolId is null)
        {
            return;
        }

        var (succeeded, resultMessage) = await AbrechnungService.AbrechnungErstellenAsync(
            selectedSchoolId.Value,
            filterBisLokal?.ToUniversalTime(),
            ComponentToken,
            currentUserId);
        SetResult(succeeded, resultMessage);
        await LoadSchoolAsync();
    }

    private Task SammelabrechnungenAusGeschlossenenKlassenErstellenAsync()
        => RunDatabaseActionAsync(SammelabrechnungenAusGeschlossenenKlassenErstellenCoreAsync);

    private async Task SammelabrechnungenAusGeschlossenenKlassenErstellenCoreAsync()
    {
        if (!isSuperAdmin)
        {
            return;
        }

        var (succeeded, resultMessage) = await AbrechnungService.SammelabrechnungenAusGeschlossenenKlassenErstellenAsync(ComponentToken);
        SetResult(succeeded, resultMessage);
        await LoadSchoolAsync();
    }

    // --- SuperAdmin: Abrechnung bestätigen ---

    private Task ConfirmBestaetigenAsync() => RunDatabaseActionAsync(ConfirmBestaetigenCoreAsync);

    private async Task ConfirmBestaetigenCoreAsync()
    {
        var periodId = confirmPeriodId;
        confirmPeriodId = null;
        if (!isSuperAdmin || periodId is null || currentUserId is not Guid superAdminId)
        {
            return;
        }

        var (succeeded, resultMessage) = await AbrechnungService.AdminBestaetigenAsync(
            periodId.Value,
            superAdminId,
            ComponentToken);
        SetResult(succeeded, resultMessage);
        await LoadSchoolAsync();
    }

    // --- SchulLeiter: akzeptieren / ablehnen ---

    private Task AcceptAsync(Guid periodId) => RunDatabaseActionAsync(() => AcceptCoreAsync(periodId));

    private async Task AcceptCoreAsync(Guid periodId)
    {
        if (isSuperAdmin)
        {
            return;
        }

        var (succeeded, resultMessage) = await AbrechnungService.SchulLeiterStatusSetzenAsync(periodId, akzeptiert: true, schulLeiterId: currentUserId);
        SetResult(succeeded, resultMessage);
        await LoadSchoolAsync();
    }

    private Task ConfirmRejectAsync() => RunDatabaseActionAsync(ConfirmRejectCoreAsync);

    private async Task ConfirmRejectCoreAsync()
    {
        var periodId = rejectPeriodId;
        var grund = rejectReason;
        rejectPeriodId = null;
        rejectReason = string.Empty;
        if (isSuperAdmin || periodId is null)
        {
            return;
        }

        var (succeeded, resultMessage) = await AbrechnungService.SchulLeiterStatusSetzenAsync(periodId.Value, akzeptiert: false, ablehnungsGrund: grund, schulLeiterId: currentUserId);
        SetResult(succeeded, resultMessage);
        await LoadSchoolAsync();
    }

    private void SetResult(bool succeeded, string resultMessage)
    {
        if (succeeded)
        {
            message = resultMessage;
            errorMessage = null;
        }
        else
        {
            errorMessage = resultMessage;
            message = null;
        }
    }

    private async Task RunDatabaseActionAsync(Func<Task> action)
    {
        if (isDatabaseActionInProgress)
        {
            return;
        }

        isDatabaseActionInProgress = true;
        try
        {
            await DatabaseActionGuard.RunAsync(action);
        }
        finally
        {
            isDatabaseActionInProgress = false;
        }
    }

    private static string StatusLabelFr(AbrechnungStatus status) => status switch
    {
        AbrechnungStatus.Erstellt => "En attente du directeur",
        AbrechnungStatus.Akzeptiert => "Acceptée et virée",
        AbrechnungStatus.Abgelehnt => "Refusée",
        AbrechnungStatus.Bestaetigt => "Confirmée et terminée",
        _ => status.ToString()
    };

    private static string StatusAccent(AbrechnungStatus status) => status switch
    {
        AbrechnungStatus.Erstellt => "amber",
        AbrechnungStatus.Akzeptiert => "cyan",
        AbrechnungStatus.Abgelehnt => "rose",
        AbrechnungStatus.Bestaetigt => "emerald",
        _ => "slate"
    };

    private static string SourceLabel(Abrechnungsperiode periode)
        => periode.AutomatischDurchKlassenabschlussErstellt
            ? "Auto: clôture de classe"
            : "Manuel: Administrateur";
}
