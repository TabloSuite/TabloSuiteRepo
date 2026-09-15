using System.Security.Claims;
using HalloApp.Core.Interfaces;
using HalloApp.Core.Models.TeacherLessons;
using HalloApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HalloApp.WebApp.Components.Pages;

public partial class StudentTeacherLessonReader
{
    [Inject]
    private TeacherLessonService TeacherLessonService { get; set; } = default!;

    [Inject]
    private IHalloAppDbContext DbContext { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public Guid LessonId { get; set; }

    private TeacherLessonDetailViewModel? lesson;
    private bool notFound;
    private int currentPageIndex;

    private TeacherLessonPageViewModel? CurrentPage =>
        lesson is not null && currentPageIndex >= 0 && currentPageIndex < lesson.Pages.Count
            ? lesson.Pages[currentPageIndex]
            : null;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var userIdValue = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdValue, out var userId))
            {
                notFound = true;
                return;
            }

            lesson = await TeacherLessonService.GetLessonDetailForStudentAsync(LessonId, userId, ComponentToken);
            notFound = lesson is null;
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private void PreviousPage()
    {
        if (currentPageIndex > 0)
        {
            currentPageIndex--;
        }
    }

    private void NextPage()
    {
        if (lesson is not null && currentPageIndex < lesson.Pages.Count - 1)
        {
            currentPageIndex++;
        }
    }

    private void GoBack()
        => Navigation.NavigateTo("/student/lessons");
}
