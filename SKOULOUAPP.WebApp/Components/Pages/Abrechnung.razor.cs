using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Models.Billing;
using SkoulouApp.Core.Models.Dashboard;
using SkoulouApp.Core.Services;
using SkoulouApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class Abrechnung
{
    [Parameter] public Guid? SchoolId { get; set; }
    [Parameter] public bool Embedded { get; set; }

    [Inject]
    private DashboardService DashboardService { get; set; } = default!;

    [Inject]
    private FinanzService FinanzService { get; set; } = default!;

    [Inject]
    private AbrechnungService AbrechnungService { get; set; } = default!;

    [Inject]
    private AbrechnungBenachrichtigungService AbrechnungBenachrichtigungService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    private IReadOnlyList<AdminSchoolOverview> schools = [];
    private bool schoolsLoaded;
    private AbrechnungsUebersicht? uebersicht;
    private ZyklusVerpflichtungsUebersicht? zyklusVerpflichtungen;
    private IReadOnlyList<Abrechnungsperiode> historie = [];
    private DateTime? historieVonDatum;
    private DateTime? historieBisDatum;
    private bool historieNurAbgeschlossene;
    private int HistorieInBearbeitungAnzahl => historie.Count(periode => periode.Status != AbrechnungStatus.Bestaetigt);
    private int HistorieAbgeschlossenAnzahl => historie.Count - HistorieInBearbeitungAnzahl;
    private IReadOnlyList<Abrechnungsperiode> GefilterteHistorie => historie
        .Where(periode => (periode.Status == AbrechnungStatus.Bestaetigt) == historieNurAbgeschlossene
            && (!historieVonDatum.HasValue || periode.BisUtc.ToLocalTime().Date >= historieVonDatum.Value.Date)
            && (!historieBisDatum.HasValue || periode.VonUtc.ToLocalTime().Date <= historieBisDatum.Value.Date))
        .ToList();
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
    private bool isKoordinator;
    private bool darfZahlungMelden;

    private bool isReadOnly => !isSuperAdmin;
    private bool CanManageBilling => isSuperAdmin || isProduktOwner;

    private IReadOnlyList<Abrechnungsperiode> PendingDirectorApprovals =>
        isSuperAdmin || isProduktOwner || !darfZahlungMelden
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
                var assignedSchoolIds = await DashboardService.GetPlatformSchoolIdsAsync(produktOwnerId, token);
                schools = (await DashboardService.GetAdminSchoolsAsync(token))
                    .Where(school => assignedSchoolIds.Contains(school.SchoolId))
                    .ToList();
                if (schools.Count == 1)
                {
                    selectedSchoolId = schools[0].SchoolId;
                    await LoadSchoolAsync();
                }

                return;
            }

            // Direktion und Koordination erhalten nur Schulen mit Finanzzugriff.
            isKoordinator = user.IsInRole("Koordinator") && !user.IsInRole("SchulLeiter");

            var directorUserIdValue = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(directorUserIdValue, out var userId))
            {
                schools = [];
                return;
            }
            currentUserId = userId;

            var financeSchools = await FinanzService.GetSchulenAsync(userId, false, token);
            var billingSchools = new List<FinanzAuswahl>();
            foreach (var school in financeSchools)
            {
                if (await FinanzService.CanAccessBillingAsync(userId, school.Id, token))
                    billingSchools.Add(school);
            }
            schools = billingSchools
                .Select(x => new AdminSchoolOverview
                {
                    SchoolId = x.Id,
                    Name = x.Name
                })
                .ToList();

            // Bei genau einer Schule automatisch auswählen.
            if (SchoolId is Guid requestedId && schools.Any(school => school.SchoolId == requestedId))
            {
                selectedSchoolId = requestedId;
                await LoadSchoolAsync();
            }
            else if (!Embedded && schools.Count == 1)
            {
                selectedSchoolId = schools[0].SchoolId;
                await LoadSchoolAsync();
            }
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
        finally
        {
            schoolsLoaded = true;
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Embedded && schoolsLoaded && SchoolId != selectedSchoolId)
        {
            selectedSchoolId = SchoolId is Guid id && schools.Any(school => school.SchoolId == id) ? id : null;
            filterBisLokal = null;
            await LoadSchoolAsync();
        }
    }

    private async Task SelectSchoolAsync(Guid schoolId)
    {
        if (Embedded || !schools.Any(school => school.SchoolId == schoolId))
        {
            return;
        }

        selectedSchoolId = schoolId;
        filterBisLokal = null;
        await LoadSchoolAsync();
    }

    private async Task LoadSchoolAsync()
    {
        zyklusVerpflichtungen = null;
        if (selectedSchoolId is null)
        {
            uebersicht = null;
            historie = [];
            automatischeOffenePerioden = [];
            return;
        }

        if (Embedded && selectedSchoolId != SchoolId || !schools.Any(school => school.SchoolId == selectedSchoolId.Value))
        {
            selectedSchoolId = null;
            uebersicht = null;
            historie = [];
            automatischeOffenePerioden = [];
            return;
        }

        var bisUtcFilter = filterBisLokal?.ToUniversalTime();
        if (currentUserId is not Guid userId || !await FinanzService.CanAccessBillingAsync(userId, selectedSchoolId.Value, ComponentToken))
        {
            uebersicht = null;
            historie = [];
            darfZahlungMelden = false;
            return;
        }
        darfZahlungMelden = !isSuperAdmin && !isProduktOwner;
        zyklusVerpflichtungen = await AbrechnungService.GetZyklusVerpflichtungenAsync(selectedSchoolId.Value, userId, ComponentToken);
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
        if (uebersicht is null || uebersicht.AnzahlPositionen == 0) return;
        if (!CanManageBilling || selectedSchoolId is null || !schools.Any(school => school.SchoolId == selectedSchoolId.Value))
        {
            errorMessage = "Sélectionne une école autorisée avant de créer la facturation.";
            return;
        }

        var (succeeded, resultMessage, periodeId) = await AbrechnungService.ManuelleAbrechnungErstellenAsync(
            selectedSchoolId.Value,
            filterBisLokal?.ToUniversalTime(),
            ComponentToken,
            currentUserId);
        SetResult(succeeded, resultMessage);
        if (succeeded && periodeId is Guid id)
        {
            confirmCreateOpen = false;
            var (gesendet, fehlgeschlagen) = await AbrechnungBenachrichtigungService.ManuelleRechnungSendenAsync(id, ComponentToken);
            if (fehlgeschlagen > 0)
                errorMessage = $"La facturation a été créée, mais la notification par e-mail n'a pas pu être envoyée à {fehlgeschlagen} destinataire(s). Consultez la facturation dans l'application.";
            else
                message = $"{resultMessage} Notification envoyée à {gesendet} directeur(s).";
        }
        await LoadSchoolAsync();
    }

    private Task RenvoyerFacturationAsync(Guid periodeId)
        => RunDatabaseActionAsync(() => RenvoyerFacturationCoreAsync(periodeId));

    private async Task RenvoyerFacturationCoreAsync(Guid periodeId)
    {
        if (!CanManageBilling || currentUserId is not Guid userId
            || !historie.Any(periode => periode.Id == periodeId
                && periode.Status == AbrechnungStatus.Erstellt
                && !periode.AutomatischDurchKlassenabschlussErstellt
                && !periode.IstSammelabrechnungAusGeschlossenenKlassen))
            return;

        var (erlaubt, gesendet, fehlgeschlagen) = await AbrechnungBenachrichtigungService.ManuelleRechnungErneutSendenAsync(
            periodeId, userId, ComponentToken);
        if (!erlaubt)
            SetResult(false, "Cette facturation ne peut plus être notifiée ou vous n'êtes pas autorisé à le faire.");
        else if (fehlgeschlagen > 0 || gesendet == 0)
            SetResult(false, $"La facturation reste inchangée. L'e-mail n'a pas pu être envoyé à {fehlgeschlagen} destinataire(s).");
        else
            SetResult(true, $"E-mail de facturation renvoyé à {gesendet} directeur(s). La facturation reste inchangée.");
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
        if (!isSuperAdmin || periodId is null || currentUserId is not Guid superAdminId)
        {
            errorMessage = "Cette confirmation est réservée à l'Administrateur.";
            return;
        }

        var (succeeded, resultMessage) = await AbrechnungService.AdminBestaetigenAsync(
            periodId.Value,
            superAdminId,
            ComponentToken);
        SetResult(succeeded, resultMessage);
        if (succeeded)
        {
            confirmPeriodId = null;
        }
        await LoadSchoolAsync();
    }

    // --- SchulLeiter: akzeptieren / ablehnen ---

    private Task AcceptAsync(Guid periodId) => RunDatabaseActionAsync(() => AcceptCoreAsync(periodId));

    private async Task AcceptCoreAsync(Guid periodId)
    {
        if (isSuperAdmin || isProduktOwner || !darfZahlungMelden || currentUserId is null
            || !historie.Any(periode => periode.Id == periodId))
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
        if (isSuperAdmin || isProduktOwner || !darfZahlungMelden || periodId is null || currentUserId is null
            || !historie.Any(periode => periode.Id == periodId))
        {
            errorMessage = "Vous n'êtes pas autorisé à refuser cette facturation.";
            return;
        }

        var (succeeded, resultMessage) = await AbrechnungService.SchulLeiterStatusSetzenAsync(periodId.Value, akzeptiert: false, ablehnungsGrund: grund, schulLeiterId: currentUserId);
        SetResult(succeeded, resultMessage);
        if (succeeded)
        {
            rejectPeriodId = null;
            rejectReason = string.Empty;
        }
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

    private Task AktualisierenAsync() => RunDatabaseActionAsync(LoadSchoolAsync);

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
