using System.Security.Claims;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.Models.Admin;
using HalloApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace HalloApp.WebApp.Components.Pages;

public partial class DirectionExamReader
{
    [Inject]
    private ExamService ExamService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public Guid ExamId { get; set; }

    [SupplyParameterFromQuery(Name = "solutions")]
    public bool? IncludeSolutionsQuery { get; set; }

    [SupplyParameterFromQuery(Name = "from")]
    public string? FromPage { get; set; }

    [SupplyParameterFromQuery(Name = "scope")]
    public string? Scope { get; set; }

    private ExamPdfViewModel? exam;
    private bool notFound;
    private bool ShowSolutions => IncludeSolutionsQuery ?? false;

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

            var includeSharedWithDirection = string.Equals(Scope, "shared", StringComparison.OrdinalIgnoreCase)
                && (state.User.IsInRole(Rollen.SchulLeiter) || state.User.IsInRole(Rollen.Koordinator));
            exam = await ExamService.GetExamForPdfAsync(userId, ExamId, includeSharedWithDirection, ComponentToken);
            notFound = exam is null;
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
            Navigation.NavigateTo("/teacher/create?mode=exam");
            return;
        }

        if (string.Equals(FromPage, "teacher-exams", StringComparison.OrdinalIgnoreCase))
        {
            Navigation.NavigateTo("/teacher-exams");
            return;
        }

        Navigation.NavigateTo("/teacher/lessons");
    }

    private string GetPdfPreviewUrl()
        => $"/teacher-exams/{ExamId}/pdf-file?solutions=false";
}
