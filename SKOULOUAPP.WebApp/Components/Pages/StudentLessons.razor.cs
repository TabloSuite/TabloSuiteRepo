using System.Security.Claims;
using HalloApp.Core.Models.Student;
using HalloApp.Core.Services;
using HalloApp.WebApp.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace HalloApp.WebApp.Components.Pages;

public partial class StudentLessons
{
    [Inject]
    private LearningService LearningService { get; set; } = default!;

    [Inject]
    private DashboardService DashboardService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private IReadOnlyList<LessonListItem>? lessons;

    private bool HasPaidHalloAppAbo { get; set; }

    private bool IsPathView { get; set; } = true;

    private int lessonJumpNumber = 1;
    private string searchTerm = string.Empty;
    private string selectedFach = string.Empty;
    private string selectedStatus = "all";
    private Guid? selectedTeacherId;

    private IReadOnlyList<ModernDropdownOption<string>> FachFilterOptions
        =>
        [
            new(string.Empty, "Toutes les matières"),
            .. FachOptions.Select(fach => new ModernDropdownOption<string>(fach, fach))
        ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> TeacherFilterOptions
        =>
        [
            new(null, "Tous les enseignants"),
            .. (lessons ?? [])
                .Where(x => x.TeacherId.HasValue && !string.IsNullOrWhiteSpace(x.TeacherName))
                .GroupBy(x => new { TeacherId = x.TeacherId!.Value, x.TeacherName })
                .Select(group => new ModernDropdownOption<Guid?>((Guid?)group.Key.TeacherId, group.Key.TeacherName))
                .OrderBy(option => option.Label)
        ];

    private static readonly IReadOnlyList<ModernDropdownOption<string>> StatusFilterOptions =
    [
        new("all", "Tous"),
        new("inprogress", "En cours"),
        new("completed", "Terminé")
    ];

    private int completedCount => lessons?.Count(x => x.IsCompleted) ?? 0;
    private int totalExercises => lessons?.Sum(x => x.ExerciseCount) ?? 0;
    private int solvedExercises => lessons?.Sum(x => x.CorrectExerciseCount) ?? 0;
    private int overallPercent => totalExercises == 0
        ? 0
        : (int)Math.Round(solvedExercises * 100.0 / totalExercises);

    private List<LessonListItem> OrderedLessons
        => lessons?.OrderBy(x => x.CourseCode).ThenBy(x => x.Order).ToList() ?? [];

    private List<string> FachOptions
        => lessons?
            .Select(x => x.FachName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList() ?? [];

    private List<LessonListItem> VisibleLessons
        => OrderedLessons.Where(PassesSearchAndFilters).ToList();

    private int VisibleCount => VisibleLessons.Count;

    private bool CanJumpToLesson
        => lessonJumpNumber >= 1 && lessonJumpNumber <= OrderedLessons.Count;

    private bool PassesSearchAndFilters(LessonListItem lesson)
    {
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            var matches = Contains(lesson.Title, term)
                || Contains(lesson.CourseCode, term)
                || Contains(lesson.Summary, term)
                || Contains(lesson.FachName, term)
                || Contains(lesson.TeacherName, term);

            if (!matches)
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedFach)
            && !string.Equals(lesson.FachName, selectedFach, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (selectedTeacherId.HasValue && lesson.TeacherId != selectedTeacherId.Value)
        {
            return false;
        }

        return selectedStatus switch
        {
            "completed" => lesson.IsCompleted,
            "inprogress" => !lesson.IsCompleted,
            _ => true
        };
    }

    private static bool Contains(string? value, string term)
        => !string.IsNullOrWhiteSpace(value)
            && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private Task SetSelectedFachAsync(string? value)
    {
        selectedFach = value ?? string.Empty;
        return Task.CompletedTask;
    }

    private Task SetSelectedStatusAsync(string? value)
    {
        selectedStatus = value ?? "all";
        return Task.CompletedTask;
    }

    private Task SetSelectedTeacherAsync(Guid? teacherId)
    {
        selectedTeacherId = teacherId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Ermittelt für jede Lektion des Lernpfads den Anzeigestatus:
    /// abgeschlossen, aktuell (erste noch nicht abgeschlossene, freigeschaltete Lektion) oder gesperrt/offen.
    /// </summary>
    private string GetPathNodeStatus(LessonListItem lesson)
    {
        if (lesson.IsCompleted)
        {
            return "completed";
        }

        if (!lesson.IsUnlocked)
        {
            return "locked";
        }

        var isNextUp = lessons is not null
            && lessons.Where(x => x.IsUnlocked && !x.IsCompleted)
                      .OrderBy(x => x.Order)
                      .FirstOrDefault() == lesson;

        return isNextUp ? "current" : "open";
    }

    private void ToggleView(bool pathView) => IsPathView = pathView;

    private string GetLessonActionText(LessonListItem lesson)
    {
        if (!lesson.IsUnlocked)
        {
            return "Classe verrouillée";
        }

        if (lesson.IsCompleted)
        {
            return "Revoir la leçon";
        }

        return lesson.CorrectExerciseCount > 0 ? "Poursuivre la leçon" : "Commencer la leçon";
    }

    private void JumpToLesson()
    {
        var ordered = OrderedLessons;
        if (lessonJumpNumber < 1 || lessonJumpNumber > ordered.Count)
        {
            return;
        }

        OpenLesson(ordered[lessonJumpNumber - 1].LessonId);
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var userIdValue = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdValue, out var userId))
            {
                lessons = [];
                return;
            }

            var dashboard = await DashboardService.GetStudentDashboardAsync(userId, ComponentToken);
            HasPaidHalloAppAbo = dashboard?.HasPaidHalloAppAbo == true;
            lessons = await LearningService.GetLessonsForStudentAsync(userId, dashboard?.CurrentLevelCode, ComponentToken);
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private void OpenLesson(Guid lessonId)
        => Navigation.NavigateTo($"/student/lessons/{lessonId}");
}
