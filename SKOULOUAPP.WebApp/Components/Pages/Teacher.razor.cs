using System.Security.Claims;
using SkoulouApp.Core.Models.Admin;
using SkoulouApp.WebApp.Components.Shared;
using SkoulouApp.WebApp.Services;
using SkoulouApp.Core.Models.Dashboard;
using SkoulouApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace SkoulouApp.WebApp.Components.Pages;

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

    private bool statusExpanded = true;

    private IReadOnlyList<ModernDropdownOption<Guid?>> SchoolFilterOptions =>
    [
        .. teacherSchools.Select(school => new ModernDropdownOption<Guid?>(school.SchoolId, $"{school.SchoolName} · {school.City}"))
    ];

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
            Label = "Score global (élevé → bas)",
            KeySelector = s => s.CombinedScorePercent ?? -1,
            SecondaryKeySelector = s => s.FullName,
            Descending = true
        },
        new()
        {
            Key = "scoreAsc",
            Label = "Score global (bas → élevé)",
            KeySelector = s => s.CombinedScorePercent ?? -1,
            SecondaryKeySelector = s => s.FullName,
            Descending = false
        },
        new()
        {
            Key = "lessons",
            Label = "Leçons terminées",
            KeySelector = s => s.CompletedLessonsCount,
            SecondaryKeySelector = s => s.FullName,
            Descending = true
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
            ApplyDefaultFilterSelection();
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
            loadErrorMessage = "Le tableau de bord n'a pas pu être chargé. Réessayez dans quelques instants.";
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

    private Guid? selectedKlasseId;

    private IReadOnlyList<ModernDropdownOption<Guid?>> KlasseFilterOptions =>
    [
        .. students
            .Where(s => s.CurrentKlasseId.HasValue && selectedSchoolId.HasValue && s.SchoolId == selectedSchoolId.Value)
            .GroupBy(s => s.CurrentKlasseId!.Value)
            .Select(g => new ModernDropdownOption<Guid?>(g.Key, g.First().CurrentLevelCode))
            .OrderBy(o => o.Label)
    ];

    private IReadOnlyList<StudentApprovalItem> filteredStudents =>
        !selectedSchoolId.HasValue || !selectedKlasseId.HasValue
            ? []
            : students
            .Where(s => s.SchoolId == selectedSchoolId.Value)
            .Where(s => s.CurrentKlasseId == selectedKlasseId.Value)
            .Where(s => !enrolledFromDate.HasValue || s.EnrolledAtUtc.Date >= enrolledFromDate.Value.Date)
            .Where(s => !enrolledToDate.HasValue || s.EnrolledAtUtc.Date <= enrolledToDate.Value.Date)
            .ToList();

    private Dictionary<Guid, StudentProgressItem> progressByUserId =>
        progress
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.First());

    private StudentProgressItem? GetProgress(Guid userId) =>
        progressByUserId.TryGetValue(userId, out var item) ? item : null;

    private void OnDateFilterChanged()
    {
    }

    private static string GetAboStatusLabel(StudentApprovalItem student)
    {
        if (student.IsBlocked) return "Compte bloqué ou en attente de validation";
        return student.CurrentKlasseId.HasValue ? "Accès aux supports publiés de la classe" : "Classe non attribuée";
    }

    private static string GetAboStatusAccent(StudentApprovalItem student)
    {
        return student.IsBlocked ? "rose" : student.CurrentKlasseId.HasValue ? "emerald" : "slate";
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

    private void ApplyDefaultFilterSelection()
    {
        selectedSchoolId = null;
        selectedKlasseId = null;
        if (teacherSchools.Count != 1)
        {
            return;
        }

        var schoolId = teacherSchools[0].SchoolId;
        var klasseIds = students
            .Where(s => s.SchoolId == schoolId && s.CurrentKlasseId.HasValue)
            .Select(s => s.CurrentKlasseId!.Value)
            .Distinct()
            .ToList();
        selectedSchoolId = schoolId;
        if (klasseIds.Count == 1)
        {
            selectedKlasseId = klasseIds[0];
        }
    }

    private void SelectSchool(Guid? schoolId)
    {
        selectedSchoolId = schoolId;
        selectedKlasseId = null;
    }

    private void SelectKlasse(Guid? klasseId)
    {
        selectedKlasseId = klasseId;
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
