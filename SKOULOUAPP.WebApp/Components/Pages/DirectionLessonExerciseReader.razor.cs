using System.Security.Claims;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.Models.Student;
using HalloApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace HalloApp.WebApp.Components.Pages;

public partial class DirectionLessonExerciseReader
{
    [Inject]
    private LearningService LearningService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public Guid LessonId { get; set; }

    private LessonDetailViewModel? lesson;
    private bool notFound;

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

            lesson = await LearningService.GetLessonDetailForDirectionReadOnlyAsync(LessonId, userId, ComponentToken);
            notFound = lesson is null;
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private void GoBack()
        => Navigation.NavigateTo("/teacher/lessons");
}
