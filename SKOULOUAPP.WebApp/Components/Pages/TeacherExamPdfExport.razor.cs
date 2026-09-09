using HalloApp.Core.Models.Admin;
using HalloApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using System.Security.Claims;

namespace HalloApp.WebApp.Components.Pages;

public partial class TeacherExamPdfExport
{
    [Inject]
    private ExamService ExamService { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Parameter]
    public Guid ExamId { get; set; }

    [SupplyParameterFromQuery(Name = "solutions")]
    public bool IncludeSolutions { get; set; }

    private ExamPdfViewModel? exam;
    private string PdfModeLabel => IncludeSolutions ? "avec solutions" : "sans solutions";

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            if (Guid.TryParse(state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var authorId))
            {
                exam = await ExamService.GetExamForPdfAsync(authorId, ExamId, ComponentToken);
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
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
