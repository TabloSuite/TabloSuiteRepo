using System.Globalization;
using System.Security.Claims;
using SkoulouApp.Core.Models.Student;
using SkoulouApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class Student
{
    [Inject]
    private DashboardService DashboardService { get; set; } = default!;

    [Inject]
    private LearningService LearningService { get; set; } = default!;

    [Inject]
    private ExamService ExamService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    private StudentDashboardViewModel? dashboard;
    private int openLessonsCount;
    private int openExamsCount;
    private const string KlassenLabel = "Classe";

    private SkoulouApp.Core.Models.Student.StudentCourseProgressItem? CurrentClassCard
        => dashboard?.CourseProgress.FirstOrDefault(course => course.IsCurrentLevel);

    private SkoulouApp.Core.Models.Student.StudentClassHistoryItem? CurrentClassHistory
        => dashboard?.ClassHistory.FirstOrDefault(course => course.IsCurrent);

    private List<SkoulouApp.Core.Models.Student.StudentClassHistoryItem> OtherClasses
        => dashboard?.ClassHistory.Where(course => !course.IsCurrent).ToList() ?? [];

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

            return dashboard.IsCurrentClassUnlocked
                ? "Cycle en cours · accès actif"
                : "Accès indisponible";
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
                return "Le cycle n'a pas encore commencé. Les contenus publiés de ta classe restent accessibles.";
            }

            if (dashboard.IsCurrentClassCycleClosed)
            {
                return "Le cycle est clôturé. Tu peux toujours consulter les contenus publiés de ta classe.";
            }

            if (!dashboard.IsCurrentClassUnlocked)
            {
                return "Ton accès est indisponible. Contacte la direction de ton école.";
            }

            return "Ton accès à la classe est actif. Tu peux continuer tes leçons et exercices.";
        }
    }

    private string ClassCycleHintAccentClass
        => dashboard is null
            ? "bg-white/10 text-white/90 ring-white/20"
            : !dashboard.HasCurrentKlasseAssignment
                ? "bg-rose-500/20 text-white ring-rose-200/40"
                : !dashboard.IsCurrentClassUnlocked
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

    private bool HasOpenItemsHint
        => dashboard is not null && (openLessonsCount > 0 || openExamsCount > 0);

    private string LastActivityText
    {
        get
        {
            if (dashboard?.LastActivityAtUtc is not DateTime lastActivity)
            {
                return "Aucune activité pour le moment";
            }

            var today = DateTime.UtcNow.Date;
            if (lastActivity.Date == today)
            {
                return "Dernière activité : aujourd’hui";
            }

            if (lastActivity.Date == today.AddDays(-1))
            {
                return "Dernière activité : hier";
            }

            return $"Dernière activité : le {lastActivity.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR"))}";
        }
    }

    private string OpenItemsHintText
    {
        get
        {
            if (dashboard is null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (openLessonsCount > 0)
            {
                parts.Add($"{openLessonsCount} leçon{(openLessonsCount > 1 ? "s" : string.Empty)} à terminer");
            }

            if (openExamsCount > 0)
            {
                parts.Add($"{openExamsCount} examen{(openExamsCount > 1 ? "s" : string.Empty)} à passer");
            }

            return string.Join(" · ", parts);
        }
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
            if (dashboard is not null)
            {
                var lessons = await LearningService.GetLessonsForStudentAsync(userId, dashboard.CurrentLevelCode, ComponentToken);
                var exams = await ExamService.GetExamsForStudentAsync(userId, dashboard.CurrentLevelCode, ComponentToken);
                openLessonsCount = lessons.Count(lesson => lesson.ExerciseCount > 0 && !lesson.IsCompleted
                    && (!lesson.ExerciseSubmissionDueAtUtc.HasValue || lesson.ExerciseSubmissionDueAtUtc.Value > DateTime.UtcNow));
                openExamsCount = exams.Count(exam => exam.CanStart && !exam.HasAttempt);
            }
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }
}
