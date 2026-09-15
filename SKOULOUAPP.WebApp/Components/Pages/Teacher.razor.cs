using System.Security.Claims;
using HalloApp.Core.Models.Admin;
using HalloApp.WebApp.Components.Shared;
using HalloApp.WebApp.Services;
using HalloApp.Core.Models.Dashboard;
using HalloApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace HalloApp.WebApp.Components.Pages;

public partial class Teacher
{
    [Inject]
    private DashboardService DashboardService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    private IReadOnlyList<TeacherSchoolOverview> teacherSchools = [];
    private IReadOnlyList<StudentApprovalItem> students = [];
    private IReadOnlyList<StudentProgressItem> progress = [];
    private Guid? selectedSchoolId;
    private bool isLoading = true;
    private string? loadErrorMessage;
    private bool isDatabaseActionInProgress;
    private bool isSchoolDirectionUser;

    [Inject]
    private DatabaseActionGuard DatabaseActionGuard { get; set; } = default!;

    private bool statusExpanded;
    private bool progressExpanded;

    private bool AllExpanded => statusExpanded && progressExpanded;

    private void ToggleAllSections()
    {
        var next = !AllExpanded;
        statusExpanded = next;
        progressExpanded = next;
    }

    private int pendingCount => filteredStudents.Count(s => s.IsBlocked);

    private bool HasMultipleSchools => teacherSchools.Count > 1;

    private IReadOnlyList<TeacherSchoolOverview> selectableSchools => teacherSchools;

    private IReadOnlyList<DataListChip<StudentApprovalItem>> studentChips =>
    [
        new()
        {
            Key = "self",
            Label = "Auto-inscription",
            Accent = "amber",
            Predicate = s => s.IsSelfRegistered,
            Count = filteredStudents.Count(s => s.IsSelfRegistered)
        }
    ];

    private IReadOnlyList<DataListSort<StudentApprovalItem>> studentSortOptions =>
    [
        new()
        {
            Key = "name",
            Label = "Nom (A-Z)",
            KeySelector = s => s.FullName,
            Descending = false
        },
        new()
        {
            Key = "scoreDesc",
            Label = "Score global (élevé ? bas)",
            KeySelector = s => s.CombinedScorePercent ?? -1,
            Descending = true
        },
        new()
        {
            Key = "scoreAsc",
            Label = "Score global (bas ? élevé)",
            KeySelector = s => s.CombinedScorePercent ?? int.MaxValue,
            Descending = false
        },
        new()
        {
            Key = "lessons",
            Label = "Leçons terminées",
            KeySelector = s => s.CompletedLessonsCount,
            Descending = true
        }
    ];

    private IReadOnlyList<DataListChip<StudentProgressItem>> progressChips =>
    [
        new()
        {
            Key = "active",
            Label = "Actifs",
            Accent = "emerald",
            Predicate = p => !p.IsBlocked,
            Count = progress.Count(p => !p.IsBlocked)
        },
        new()
        {
            Key = "blocked",
            Label = "Bloqués",
            Accent = "rose",
            Predicate = p => p.IsBlocked,
            Count = progress.Count(p => p.IsBlocked)
        }
    ];


    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        isSchoolDirectionUser = authState.User.IsInRole("SchulLeiter") || authState.User.IsInRole("Koordinator");
        var userIdValue = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            teacherSchools = [];
            students = [];
            progress = [];
            isLoading = false;
            loadErrorMessage = "Impossible d'identifier votre compte enseignant.";
            return;
        }

        var token = ComponentToken;
        isLoading = true;
        loadErrorMessage = null;
        try
        {
            teacherSchools = await DashboardService.GetTeacherSchoolsAsync(userId, token);
            students = await DashboardService.GetTeacherStudentsAsync(userId, token);
            progress = await DashboardService.GetTeacherStudentProgressAsync(userId, token);
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (z. B. schneller
            // Navigationswechsel). Ergebnisse werden nicht mehr benötigt.
        }
        catch
        {
            teacherSchools = [];
            students = [];
            progress = [];
            loadErrorMessage = "Le tableau de bord n'a pas pu être chargé. Réessaie dans quelques instants.";
        }
        finally
        {
            isLoading = false;
        }
    }

    private Guid? expandedLevelStudentId;
    private IReadOnlyList<LevelEnrollmentItem> levelEnrollments = [];

    private DateTime? enrolledFromDate;
    private DateTime? enrolledToDate;

    private IReadOnlyList<StudentApprovalItem> filteredStudents =>
        students
            .Where(s => !selectedSchoolId.HasValue || s.SchoolId == selectedSchoolId.Value)
            .Where(s => !enrolledFromDate.HasValue || s.EnrolledAtUtc.Date >= enrolledFromDate.Value.Date)
            .Where(s => !enrolledToDate.HasValue || s.EnrolledAtUtc.Date <= enrolledToDate.Value.Date)
            .ToList();

    private IReadOnlyList<StudentProgressItem> filteredProgress =>
        progress
            .Where(item => !selectedSchoolId.HasValue || item.SchoolId == selectedSchoolId.Value)
            .ToList();

    private void OnDateFilterChanged()
    {
    }

    private static string GetAboStatusLabel(StudentApprovalItem student)
    {
        if (!student.CurrentKlasseId.HasValue)
        {
            return "Classe non attribuée";
        }

        if (!student.HasActiveSchoolFeeCycle)
        {
            return "Cycle non démarré";
        }

        if (!student.HasActiveSubscription)
        {
            return "Aucun abo actif";
        }

        return student.HasPaidEnrollment
            ? "Abo actif (payé)"
            : "Abo actif";
    }

    private static string GetAboStatusAccent(StudentApprovalItem student)
    {
        if (!student.CurrentKlasseId.HasValue)
        {
            return "slate";
        }

        if (!student.HasActiveSchoolFeeCycle)
        {
            return "amber";
        }

        if (!student.HasActiveSubscription)
        {
            return "rose";
        }

        return student.HasPaidEnrollment ? "emerald" : "cyan";
    }

    private void ClearDateFilter()
    {
        enrolledFromDate = null;
        enrolledToDate = null;
    }

    private async Task ToggleLevelPanelAsync(Guid studentId)
    {
        if (expandedLevelStudentId == studentId)
        {
            expandedLevelStudentId = null;
            levelEnrollments = [];
            return;
        }

        expandedLevelStudentId = studentId;
        levelEnrollments = await DashboardService.GetStudentLevelEnrollmentsAsync(studentId);
    }

    private Task ToggleUnlockedAsync(Guid studentId, Guid klasseId, bool isUnlocked) => RunDatabaseActionAsync(() => ToggleUnlockedCoreAsync(studentId, klasseId, isUnlocked));

    private async Task ToggleUnlockedCoreAsync(Guid studentId, Guid klasseId, bool isUnlocked)
    {
        await DashboardService.SetLevelUnlockedAsync(studentId, klasseId, isUnlocked);
        levelEnrollments = await DashboardService.GetStudentLevelEnrollmentsAsync(studentId);
    }

    private Task TogglePaidAsync(Guid studentId, Guid klasseId, bool isPaid) => RunDatabaseActionAsync(() => TogglePaidCoreAsync(studentId, klasseId, isPaid));

    private async Task TogglePaidCoreAsync(Guid studentId, Guid klasseId, bool isPaid)
    {
        await DashboardService.SetLevelPaidAsync(studentId, klasseId, isPaid);
        levelEnrollments = await DashboardService.GetStudentLevelEnrollmentsAsync(studentId);
    }

    private void SelectSchool(Guid? schoolId)
    {
        selectedSchoolId = schoolId;
    }

    private async Task RunDatabaseActionAsync(Func<Task> action)
    {
        if (isDatabaseActionInProgress)
        {
            return;
        }

        isDatabaseActionInProgress = true;
        try
        {
            await DatabaseActionGuard.RunAsync(action);
        }
        finally
        {
            isDatabaseActionInProgress = false;
        }
    }
}
