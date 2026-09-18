using System.Security.Claims;
using HalloApp.Core.Models.Student;
using HalloApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace HalloApp.WebApp.Components.Pages;

public partial class StudentExams
{
    [Inject]
    private ExamService ExamService { get; set; } = default!;

    [Inject]
    private DashboardService DashboardService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private IReadOnlyList<ExamListItem>? exams;
    private bool hasPaidHalloAppAbo;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var userIdValue = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdValue, out var userId))
            {
                exams = [];
                return;
            }

            var dashboard = await DashboardService.GetStudentDashboardAsync(userId, ComponentToken);
            hasPaidHalloAppAbo = dashboard?.HasPaidHalloAppAbo == true;
            exams = await ExamService.GetExamsForStudentAsync(userId, dashboard?.CurrentLevelCode, ComponentToken);
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private void StartExam(Guid examId)
        => Navigation.NavigateTo($"/student/exams/{examId}");
}
