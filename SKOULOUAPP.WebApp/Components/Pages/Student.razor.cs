using System.Security.Claims;
using HalloApp.Core.Models.Student;
using HalloApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace HalloApp.WebApp.Components.Pages;

public partial class Student
{
    [Inject]
    private DashboardService DashboardService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    private StudentDashboardViewModel? dashboard;
    private const string KlassenLabel = "Classe";

    private string CurrentClassDisplay
        => dashboard is null
            ? "—"
            : dashboard.HasCurrentKlasseAssignment && !string.IsNullOrWhiteSpace(dashboard.CurrentLevelCode)
                ? dashboard.CurrentLevelCode
                : "Aucune";

    private string ClassCycleBadgeText
    {
        get
        {
            if (dashboard is null)
            {
                return string.Empty;
            }

            if (!dashboard.HasCurrentKlasseAssignment)
            {
                return "Aucune classe attribuée";
            }

            if (!dashboard.HasCurrentClassCycle)
            {
                return "Cycle non démarré";
            }

            if (dashboard.IsCurrentClassCycleClosed)
            {
                return "Cycle clôturé";
            }

            if (!dashboard.IsCurrentClassPaid)
            {
                return "Cycle démarré · abonnement non payé";
            }

            return dashboard.IsCurrentClassUnlocked
                ? "Cycle démarré · abonnement actif"
                : "Classe verrouillée";
        }
    }

    private string ClassCycleHintText
    {
        get
        {
            if (dashboard is null)
            {
                return string.Empty;
            }

            if (!dashboard.HasCurrentKlasseAssignment)
            {
                return "Ton école doit d'abord te rattacher à une classe active.";
            }

            if (!dashboard.HasCurrentClassCycle)
            {
                return "Le cycle de ta classe n'a pas encore commencé. Les contenus seront disponibles après démarrage.";
            }

            if (dashboard.IsCurrentClassCycleClosed)
            {
                return "Le cycle de ta classe est clôturé. Les nouvelles leçons dépendront de la prochaine affectation.";
            }

            if (!dashboard.IsCurrentClassPaid)
            {
                return "Le cycle de ta classe a démarré, mais ton abonnement n'est pas encore payé. Demande la validation auprès de la direction.";
            }

            if (!dashboard.IsCurrentClassUnlocked)
            {
                return "Demande à la direction d'activer ton abonnement de classe pour accéder aux contenus de tes enseignants.";
            }

            return "Ton accès à la classe est actif. Tu peux continuer tes leçons et exercices.";
        }
    }

    private string ClassCycleHintAccentClass
        => dashboard is null
            ? "bg-white/10 text-white/90 ring-white/20"
            : (!dashboard.HasCurrentKlasseAssignment || dashboard.IsCurrentClassCycleClosed)
                ? "bg-rose-500/20 text-white ring-rose-200/40"
                : (!dashboard.HasCurrentClassCycle || !dashboard.IsCurrentClassPaid || !dashboard.IsCurrentClassUnlocked)
                    ? "bg-amber-500/20 text-white ring-amber-200/40"
                    : "bg-emerald-500/20 text-white ring-emerald-200/40";

    private string ClassHistoryEmptyMessage
        => dashboard is null
            ? string.Empty
            : !dashboard.HasCurrentKlasseAssignment
                ? "Aucune classe n'est encore attribuée."
                : !dashboard.HasCurrentClassCycle
                    ? "Ta classe est attribuée, mais son cycle n'a pas encore démarré."
                    : dashboard.IsCurrentClassCycleClosed
                        ? "Le cycle de ta classe est clôturé. Les nouvelles leçons dépendront de la prochaine affectation."
                        : "Aucune leçon publiée pour ta classe actuellement.";

    private static string FormatClasseLabel(string? code, string? title)
    {
        var normalizedCode = code?.Trim() ?? string.Empty;
        var normalizedTitle = title?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return normalizedTitle;
        }

        if (string.IsNullOrWhiteSpace(normalizedTitle)
            || string.Equals(normalizedCode, normalizedTitle, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedCode;
        }

        return $"{normalizedCode} · {normalizedTitle}";
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var userIdValue = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdValue, out var userId))
            {
                return;
            }

            dashboard = await DashboardService.GetStudentDashboardAsync(userId, ComponentToken);
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }
}
