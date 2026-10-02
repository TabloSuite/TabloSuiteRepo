using System.Security.Claims;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.Models.Student;
using SkoulouApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace SkoulouApp.WebApp.Components.Pages;

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

    [SupplyParameterFromQuery(Name = "from")]
    public string? FromPage { get; set; }

    private LessonDetailViewModel? lesson;
    private bool notFound;

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            if ((!state.User.IsInRole(Rollen.SchulLeiter) && !state.User.IsInRole(Rollen.Koordinator) && !state.User.IsInRole(Rollen.Lehrer))
                || !Guid.TryParse(state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            {
                notFound = true;
                return;
            }

            lesson = await LearningService.GetLessonDetailForTeacherOrDirectionReadOnlyAsync(LessonId, userId, ComponentToken);
            notFound = lesson is null;
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private void GoBack()
    {
        if (string.Equals(FromPage, "teacher-create", StringComparison.OrdinalIgnoreCase))
        {
            Navigation.NavigateTo("/teacher/create");
            return;
        }

        Navigation.NavigateTo("/teacher/lessons");
    }
}
