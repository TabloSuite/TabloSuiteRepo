using System.Security.Claims;
using HalloApp.Core.Models.TeacherLessons;
using HalloApp.Core.Services;
using HalloApp.WebApp.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace HalloApp.WebApp.Components.Pages;

public partial class StudentTeacherLessons
{
    [Inject]
    private TeacherLessonService TeacherLessonService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private IReadOnlyList<TeacherLessonListItem>? lessons;
    private IReadOnlyList<TeacherLessonListItem> currentLessons = [];
    private IReadOnlyList<TeacherLessonListItem> historyLessons = [];

    private Guid? selectedTeacherId;
    private Guid? selectedFachId;

    private IReadOnlyList<ModernDropdownOption<Guid?>> TeacherOptions =>
    [
        new(null, "Tous les enseignants"),
        .. (lessons ?? [])
            .Where(item => item.AuthorId.HasValue)
            .GroupBy(item => item.AuthorId!.Value)
            .Select(group => new ModernDropdownOption<Guid?>(group.Key, group.First().AuthorName))
            .OrderBy(option => option.Label)
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> FachOptions =>
    [
        new(null, "Toutes les matières"),
        .. (lessons ?? [])
            .Where(item => item.FachId.HasValue && !string.IsNullOrWhiteSpace(item.FachName))
            .GroupBy(item => item.FachId!.Value)
            .Select(group => new ModernDropdownOption<Guid?>(group.Key, group.First().FachName!))
            .OrderBy(option => option.Label)
    ];

    private IReadOnlyList<TeacherLessonListItem> FilteredCurrentLessons => ApplyFilters(currentLessons);
    private IReadOnlyList<TeacherLessonListItem> FilteredHistoryLessons => ApplyFilters(historyLessons);

    private IReadOnlyList<TeacherLessonListItem> ApplyFilters(IReadOnlyList<TeacherLessonListItem> source)
        => source
            .Where(item => !selectedTeacherId.HasValue || item.AuthorId == selectedTeacherId)
            .Where(item => !selectedFachId.HasValue || item.FachId == selectedFachId)
            .ToList();

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

            lessons = await TeacherLessonService.GetLessonsForStudentAsync(userId, ComponentToken);
            currentLessons = lessons.Where(item => item.IsCurrentKlasse).ToList();
            historyLessons = lessons.Where(item => !item.IsCurrentKlasse).ToList();
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private void OpenLesson(Guid lessonId)
        => Navigation.NavigateTo($"/student/cours/{lessonId}");
}
