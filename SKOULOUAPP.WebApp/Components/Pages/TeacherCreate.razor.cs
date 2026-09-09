using Microsoft.AspNetCore.Components;

namespace HalloApp.WebApp.Components.Pages;

public partial class TeacherCreate
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "mode")]
    public string? Mode { get; set; }

    private TeacherCreateMode activeMode = TeacherCreateMode.Lesson;

    protected override void OnParametersSet()
    {
        activeMode = Mode?.ToLowerInvariant() switch
        {
            "exam" => TeacherCreateMode.Exam,
            _ => TeacherCreateMode.Lesson
        };
    }

    private void SetMode(TeacherCreateMode mode)
    {
        activeMode = mode;
        var modeValue = mode == TeacherCreateMode.Exam ? "exam" : "lesson";
        Navigation.NavigateTo($"/teacher/create?mode={modeValue}", replace: true);
    }

    protected enum TeacherCreateMode
    {
        Lesson,
        Exam
    }
}
