using System.Security.Claims;
using SkoulouApp.Core.Models.TeacherLessons;
using SkoulouApp.Core.Models.Student;
using SkoulouApp.Core.Services;
using SkoulouApp.WebApp.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class StudentHistory
{
    [Inject]
    private TeacherLessonService TeacherLessonService { get; set; } = default!;

    [Inject]
    private DashboardService DashboardService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private IReadOnlyList<TeacherLessonListItem>? historyLessons;
    private IReadOnlyList<StudentClassHistoryItem> classHistory = [];
    private Guid? selectedTeacherId;
    private Guid? selectedFachId;

    private IReadOnlyList<ModernDropdownOption<Guid?>> TeacherOptions =>
    [
        new((Guid?)null, "Tous les enseignants"),
        .. (historyLessons ?? [])
            .Where(item => item.AuthorId.HasValue)
            .GroupBy(item => item.AuthorId!.Value)
            .Select(group => new ModernDropdownOption<Guid?>((Guid?)group.Key, group.First().AuthorName))
            .OrderBy(option => option.Label)
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> FachOptions =>
    [
        new((Guid?)null, "Toutes les matières"),
        .. (historyLessons ?? [])
            .Where(item => item.FachId.HasValue && !string.IsNullOrWhiteSpace(item.FachName))
            .GroupBy(item => item.FachId!.Value)
            .Select(group => new ModernDropdownOption<Guid?>((Guid?)group.Key, group.First().FachName!))
            .OrderBy(option => option.Label)
    ];

    private IReadOnlyList<TeacherLessonListItem> FilteredHistoryLessons => (historyLessons ?? [])
        .Where(item => !selectedTeacherId.HasValue || item.AuthorId == selectedTeacherId)
        .Where(item => !selectedFachId.HasValue || item.FachId == selectedFachId)
        .Where(item => string.IsNullOrWhiteSpace(historySuche)
            || item.Title.Contains(historySuche.Trim(), StringComparison.CurrentCultureIgnoreCase)
            || item.CourseCode.Contains(historySuche.Trim(), StringComparison.CurrentCultureIgnoreCase)
            || (item.KlasseTitle ?? string.Empty).Contains(historySuche.Trim(), StringComparison.CurrentCultureIgnoreCase)
            || (item.Summary ?? string.Empty).Contains(historySuche.Trim(), StringComparison.CurrentCultureIgnoreCase))
        .ToList();

    private string historySuche = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var userIdValue = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdValue, out var userId))
            {
                historyLessons = [];
                return;
            }

            historyLessons = await TeacherLessonService.GetHistoryLessonsForStudentAsync(userId, ComponentToken);
            classHistory = (await DashboardService.GetStudentDashboardAsync(userId, ComponentToken))?.ClassHistory ?? [];
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private Task SetTeacherFilterAsync(Guid? teacherId)
    {
        selectedTeacherId = teacherId;
        return Task.CompletedTask;
    }

    private Task SetFachFilterAsync(Guid? fachId)
    {
        selectedFachId = fachId;
        return Task.CompletedTask;
    }

    private void OpenLesson(Guid lessonId)
        => Navigation.NavigateTo($"/student/lessons/support/{lessonId}");
}
