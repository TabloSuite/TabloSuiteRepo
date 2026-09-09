using System.Security.Claims;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.Models.TeacherLessons;
using HalloApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace HalloApp.WebApp.Components.Pages;

public partial class TeacherLessonPdfExport
{
    [Inject]
    private TeacherLessonService TeacherLessonService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Parameter]
    public Guid LessonId { get; set; }

    [SupplyParameterFromQuery(Name = "solutions")]
    public bool IncludeSolutions { get; set; } = true;

    private TeacherLessonDetailViewModel? lesson;

    private string PdfModeLabel => IncludeSolutions ? "avec solutions" : "sans solutions";

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            if (!Guid.TryParse(state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            {
                return;
            }

            var canExportSharedDirectionContent = state.User.IsInRole(Rollen.SchulLeiter)
                || state.User.IsInRole(Rollen.Koordinator);

            lesson = await TeacherLessonService.GetLessonDetailForPdfExportAsync(
                LessonId, userId, canExportSharedDirectionContent, ComponentToken);
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
            // Der asynchrone Blazor-Interop-Aufruf darf nicht auf den schließbaren
            // Browser-Druckdialog warten. Der Timer gibt den Aufruf sofort zurück.
            await JS.InvokeVoidAsync("SKOULOUAPPPrint.open");
        }
        catch (TaskCanceledException)
        {
            // Der Browser kann den interaktiven Circuit beim Öffnen/Schließen des
            // Druckdialogs anhalten; der Druckvorgang läuft dabei bereits lokal.
        }
        catch (JSDisconnectedException)
        {
            // Navigation oder ein getrennter Circuit nach dem lokalen Druckdialog.
        }
    }
}
