using System.Security.Claims;
using HalloApp.Core.Models.TeacherLessons;
using HalloApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace HalloApp.WebApp.Components.Pages;

public partial class StudentTeacherLessonPdfExport
{
    [Inject]
    private TeacherLessonService TeacherLessonService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Parameter]
    public Guid LessonId { get; set; }

    private TeacherLessonDetailViewModel? lesson;

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            if (Guid.TryParse(state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            {
                lesson = await TeacherLessonService.GetLessonDetailForStudentPdfExportAsync(LessonId, userId, ComponentToken);
            }
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private async Task PrintAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("SKOULOUAPPPrint.open");
        }
        catch (TaskCanceledException)
        {
            // Der lokale Browser-Druckdialog kann den Circuit anhalten.
        }
        catch (JSDisconnectedException)
        {
            // Navigation oder ein getrennter Circuit nach dem Druckdialog.
        }
    }
}