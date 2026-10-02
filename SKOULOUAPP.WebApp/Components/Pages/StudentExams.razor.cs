using System.Security.Claims;
using SkoulouApp.Core.Models.Student;
using SkoulouApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class StudentExams : IDisposable
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
    private bool hasPaidSkoulouAppAbo;
    private Timer? countdownTimer;
    private DateTime nowUtc = DateTime.UtcNow;

    private string GetRemainingTime(ExamListItem exam)
    {
        if (exam.FirstDeadlineUtc is not DateTime deadline) return string.Empty;
        var remaining = deadline - nowUtc;
        if (remaining <= TimeSpan.Zero) return "Temps écoulé";
        return $"Temps restant : {(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

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
            hasPaidSkoulouAppAbo = dashboard?.HasPaidSkoulouAppAbo == true;
            exams = await ExamService.GetExamsForStudentAsync(userId, dashboard?.CurrentLevelCode, ComponentToken);
            if (exams.Any(exam => exam.HasActiveAttempt && exam.CanStart))
            {
                countdownTimer = new Timer(async _ =>
                {
                    try
                    {
                        await InvokeAsync(async () =>
                        {
                            nowUtc = DateTime.UtcNow;
                            if (exams?.Any(exam => exam.HasActiveAttempt && !exam.CanStart) == true)
                            {
                                countdownTimer?.Dispose();
                                countdownTimer = null;
                                exams = await ExamService.GetExamsForStudentAsync(userId, cancellationToken: ComponentToken);
                            }
                            StateHasChanged();
                        });
                    }
                    catch (Exception ex) when (IstAbbruch(ex)) { }
                }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private void StartExam(Guid examId)
        => Navigation.NavigateTo($"/student/exams/{examId}");

    public override void Dispose()
    {
        countdownTimer?.Dispose();
        base.Dispose();
    }
}
