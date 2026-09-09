using System.Security.Claims;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.Models.TeacherLessons;
using HalloApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace HalloApp.WebApp.Components.Pages;

public partial class TeacherLessonDirectionReader
{
    [Inject]
    private TeacherLessonService TeacherLessonService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public Guid LessonId { get; set; }

    private TeacherLessonDetailViewModel? lesson;
    private bool notFound;
    private int currentPageIndex;

    private TeacherLessonPageViewModel? CurrentPage => lesson?.Pages.ElementAtOrDefault(currentPageIndex);

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            if ((!state.User.IsInRole(Rollen.SchulLeiter) && !state.User.IsInRole(Rollen.Koordinator))
                || !Guid.TryParse(state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            {
                notFound = true;
                return;
            }

            lesson = await TeacherLessonService.GetLessonDetailForDirectionAsync(LessonId, userId, ComponentToken);
            notFound = lesson is null;
            currentPageIndex = 0;
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
        => Navigation.NavigateTo("/teacher/lessons");
}
