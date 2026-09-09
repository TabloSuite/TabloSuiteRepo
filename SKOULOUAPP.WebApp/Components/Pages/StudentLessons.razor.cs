using System.Security.Claims;
using HalloApp.Core.Models.Student;
using HalloApp.Core.Services;
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

    private int completedCount => lessons?.Count(x => x.IsCompleted) ?? 0;
    private int totalExercises => lessons?.Sum(x => x.ExerciseCount) ?? 0;
    private int solvedExercises => lessons?.Sum(x => x.CorrectExerciseCount) ?? 0;
    private int overallPercent => totalExercises == 0
        ? 0
        : (int)Math.Round(solvedExercises * 100.0 / totalExercises);

    private List<LessonListItem> OrderedLessons
        => lessons?.OrderBy(x => x.CourseCode).ThenBy(x => x.Order).ToList() ?? [];

    private bool CanJumpToLesson
        => lessonJumpNumber >= 1 && lessonJumpNumber <= OrderedLessons.Count;

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
            return "Refaire";
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
