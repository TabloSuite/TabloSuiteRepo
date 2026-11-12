using System.Security.Claims;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Models.Admin;
using SkoulouApp.Core.Models.Dashboard;
using SkoulouApp.Core.Models.Import;
using SkoulouApp.Core.Services;
using SkoulouApp.WebApp.Components.Shared;
using SkoulouApp.WebApp.Services;
using SkoulouApp.Core.Models.TeacherLessons;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class SchulLeiter
{
    [Inject] private ILogger<SchulLeiter> Logger { get; set; } = default!;
    [Inject] private ExamService ExamService { get; set; } = default!;

    private IReadOnlyList<AbschlussMention> schulMentions = [];
    private Guid? mentionEditId;
    private string mentionText = string.Empty;
    private string? mentionMessage;
    private string? mentionError;

    private async Task SaveMentionAsync()
    {
        if (SelectedKlasseSchoolId is not Guid schoolId) return;
        var result = await ExamService.SaveAbschlussMentionAsync(schoolId, mentionEditId, mentionText, currentUserId, ComponentToken);
        mentionError = result.Succeeded ? null : result.Message;
        mentionMessage = result.Succeeded ? result.Message : null;
        if (result.Succeeded)
        {
            mentionEditId = null;
            mentionText = string.Empty;
            schulMentions = await ExamService.GetAbschlussMentionsAsync(schoolId, currentUserId, ComponentToken);
        }
    }

    private void EditMention(AbschlussMention mention)
    {
        mentionEditId = mention.Id;
        mentionText = mention.Text;
    }

    private async Task DeleteMentionAsync(Guid mentionId)
    {
        if (SelectedKlasseSchoolId is not Guid schoolId) return;
        var result = await ExamService.DeleteAbschlussMentionAsync(schoolId, mentionId, currentUserId, ComponentToken);
        mentionError = result.Succeeded ? null : result.Message;
        mentionMessage = result.Succeeded ? result.Message : null;
        if (result.Succeeded) schulMentions = await ExamService.GetAbschlussMentionsAsync(schoolId, currentUserId, ComponentToken);
    }

    // Reiter-Schlüssel = Wert des Query-Parameters « onglet » (z. B. /schulleiter?onglet=classes aus dem Tableau de bord).
    private const string DirectorTabStudents = "eleves";
    private const string DirectorTabSubjects = "matieres";
    private const string DirectorTabClasses = "classes";
    private const string DirectorTabUsers = "equipe";
    private const string DirectorTabReassign = "reattribution";
    private const string DirectorTabImport = "import";
    private const string DirectorTabShared = "partages";

    /// <summary>Beträge wie im Tableau de bord: « 75 000 FCFA », unabhängig von der Serverkultur.</summary>
    private static readonly CultureInfo Fr = SkoulouApp.Core.Services.SkoulouKultur.Instanz;

    private static readonly string[] DirectorTabs =
        [DirectorTabStudents, DirectorTabClasses, DirectorTabUsers, DirectorTabSubjects, DirectorTabReassign, DirectorTabImport, DirectorTabShared];

    [SupplyParameterFromQuery(Name = "onglet")]
    public string? Onglet { get; set; }

    private string activeDirectorTab = DirectorTabStudents;

    private bool IsDirectorTabActive(string tabKey)
        => string.Equals(activeDirectorTab, tabKey, StringComparison.Ordinal);

    private void SetDirectorTab(string tabKey)
        => activeDirectorTab = tabKey;

    private IReadOnlyList<Reiter.Eintrag> DirectorReiter =>
    [
        new(DirectorTabStudents, "Élèves", "graduation", ActiveStudentCount, ActivePendingCount, "Inscription(s) en attente de validation"),
        new(DirectorTabClasses, "Classes", "layers", schoolKlassen.Count),
        new(DirectorTabUsers, isProduktOwner ? "Directeurs" : "Équipe", "users", FilteredSchoolUsersOhneSchueler.Count),
        new(DirectorTabSubjects, "Matières", "book", schoolFachOptions.Count),
        new(DirectorTabReassign, "Réattribution", "refresh", null, StudentsWithoutActiveClass.Count, "Élève(s) sans classe active"),
        new(DirectorTabImport, "Import", "upload"),
        .. canViewSharedTeacherContent
            ? new Reiter.Eintrag[] { new(DirectorTabShared, "Contenus partagés", "book-open", FilteredSharedTeacherContent.Count) }
            : []
    ];

    private string DirectorRubrik => isProduktOwner ? "Établissements suivis" : isKoordinator ? "Coordination" : "Direction";

    private string DirectorUntertitel
    {
        get
        {
            var schule = SelectedSchoolForManagement ?? (schools.Count == 1 ? schools[0] : null);
            if (schule is null)
            {
                return $"{schools.Count} établissement(s) · {ActiveStudentCount} élève(s)";
            }

            return string.IsNullOrWhiteSpace(schule.City) ? schule.SchoolName : $"{schule.SchoolName} · {schule.City}";
        }
    }

    /// <summary>Neues Konto mit vorausgewählter Rolle « Élève » (Reiter Élèves).</summary>
    private async Task OpenNewStudentModalAsync()
    {
        await OpenNewUserModalAsync();
        if (!AllowedSchoolUserRoles.Contains(Rollen.Schueler))
        {
            return;
        }

        userForm.RoleName = Rollen.Schueler;
        if (SelectedKlasseSchoolId is Guid schoolId)
        {
            SelectedStudentSchoolId = schoolId;
            await LoadUserFormKlassenAsync();
        }
    }

    private bool KannIdentifiantVorschlagen => !string.IsNullOrWhiteSpace(userForm.Vorname)
        && !string.IsNullOrWhiteSpace(userForm.Nachname);

    /// <summary>Schlägt ein eindeutiges Identifiant „vorname.nachname“ vor (änderbar).</summary>
    private async Task IdentifiantVorschlagenAsync()
    {
        if (KannIdentifiantVorschlagen)
        {
            userForm.Identifiant = await AuthService.IdentifiantVorschlagAsync(userForm.Vorname, userForm.Nachname, ComponentToken);
        }
    }

    [Inject]
    private DashboardService DashboardService { get; set; } = default!;

    [Inject]
    private AboService AboService { get; set; } = default!;

    [Inject]
    private TeacherLessonService LessonService { get; set; } = default!;

    [Inject]
    private FinanzService FinanzService { get; set; } = default!;


    [Inject]
    private SchoolUserImportService SchoolUserImportService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private AuthService AuthService { get; set; } = default!;

    [Inject]
    private KontoBenachrichtigungService KontoBenachrichtigungService { get; set; } = default!;

    private IReadOnlyList<TeacherSchoolOverview> schools = [];
    private IReadOnlyList<StudentApprovalItem> students = [];
    private IReadOnlyList<LevelEnrollmentItem> levelEnrollments = [];

    private IReadOnlyList<SchoolUserItem> schoolUsers = [];
    private IReadOnlyList<CourseFormModel> userFormKlassen = [];
    private IReadOnlyList<LehrerNiveauZuordnungItem> userFormTeacherClassOptions = [];
    private string? teacherClassReferenceFilter;

    // Filter auf eine bestimmte Schule (null = alle Schulen).
    private string? schoolFilterName;
    private Guid? studentKlasseFilterId;
    private Guid? studentKlassenStufeFilterId;

    private bool HasMultipleSchools => schools.Count > 1;

    private int TotalStudentCount => schools.Sum(s => s.StudentCount);
    private int TotalPendingCount => schools.Sum(s => s.PendingApprovalCount);
    private int TotalBlockedCount => schools.Sum(s => s.BlockedStudentCount);

    private int ActiveStudentCount => SelectedSchoolForManagement?.StudentCount ?? TotalStudentCount;
    private int ActivePendingCount => SelectedSchoolForManagement?.PendingApprovalCount ?? TotalPendingCount;

    private IReadOnlyList<ModernDropdownOption<string?>> SchoolFilterOptions =>
    [
        new ModernDropdownOption<string?>(null, "Toutes les écoles"),
        .. schools.Select(school => new ModernDropdownOption<string?>(school.SchoolName, $"{school.SchoolName} · {school.City}"))
    ];

    private IReadOnlyList<StudentApprovalItem> FilteredStudents =>
        students
            .Where(student => string.IsNullOrWhiteSpace(schoolFilterName) || student.SchoolName == schoolFilterName)
            .Where(student => StudentMatchesKlassenStufeFilter(student))
            .Where(student => StudentMatchesKlasseFilter(student))
            .ToList();

    private bool StudentMatchesKlassenStufeFilter(StudentApprovalItem student)
    {
        if (studentKlassenStufeFilterId is not Guid klassenStufeId)
        {
            return true;
        }

        CourseFormModel? currentKlasse = null;
        if (student.CurrentKlasseId is Guid currentKlasseId)
        {
            currentKlasse = schoolKlassen.FirstOrDefault(klasse => klasse.CourseId == currentKlasseId);
        }

        currentKlasse ??= schoolKlassen.FirstOrDefault(klasse =>
            string.Equals(klasse.Code, student.CurrentLevelCode, StringComparison.OrdinalIgnoreCase));

        if (currentKlasse is null)
        {
            var selectedStufeName = klassenStufen.FirstOrDefault(stufe => stufe.Id == klassenStufeId)?.Name;
            if (!string.IsNullOrWhiteSpace(selectedStufeName)
                && string.Equals(student.CurrentLevelCode, selectedStufeName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return currentKlasse?.KlassenStufeId == klassenStufeId;
    }

    private bool StudentMatchesKlasseFilter(StudentApprovalItem student)
    {
        if (studentKlasseFilterId is not Guid klasseId)
        {
            return true;
        }

        if (student.CurrentKlasseId == klasseId)
        {
            return true;
        }

        var selectedKlasse = schoolKlassen.FirstOrDefault(klasse => klasse.CourseId == klasseId);

        if (student.CurrentKlasseId is null
            && selectedKlasse is not null
            && !string.IsNullOrWhiteSpace(selectedKlasse.ReferenceName)
            && string.Equals(student.CurrentLevelCode, selectedKlasse.ReferenceName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return student.CurrentKlasseId is null
               && selectedKlasse is not null
               && string.Equals(student.CurrentLevelCode, selectedKlasse.Code, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<CourseFormModel> StudentKlassenFilterOptions => schoolKlassen
        .Where(klasse => !studentKlassenStufeFilterId.HasValue || klasse.KlassenStufeId == studentKlassenStufeFilterId)
        .OrderBy(klasse => klasse.Code)
        .ThenBy(klasse => klasse.Title)
        .ToList();

    private IReadOnlyList<ModernDropdownOption<Guid?>> StudentKlassenStufeFilterOptions =>
    [
        new(null, "Tous les noms de référence"),
        .. klassenStufen
            .OrderBy(stufe => stufe.Name)
            .Select(stufe => new ModernDropdownOption<Guid?>((Guid?)stufe.Id, stufe.Name))
    ];

    private IReadOnlyList<FachOptionItem> SchoolFachOptionsForSelectedSchool
        => SelectedSchoolForManagement is { SchoolId: var schoolId }
            ? schoolFachOptions.Where(option => option.SchoolId == schoolId).OrderBy(option => option.Name).ToList()
            : [];

    private Guid? ActiveFachSchoolId => SelectedSchoolForManagement?.SchoolId;

    private bool CanAddSchoolFach
        => ActiveFachSchoolId.HasValue && !string.IsNullOrWhiteSpace(neueSchulFachBezeichnung);

    private IReadOnlyList<FachOptionItem> UserFormTeacherFachOptions
        => userForm.RoleName == Rollen.Lehrer
            ? schoolFachOptions
                .Where(option => userForm.SchoolIds.Contains(option.SchoolId))
                .OrderBy(option => option.SchoolName)
                .ThenBy(option => option.Name)
                .ToList()
            : [];

    private IReadOnlyList<ModernDropdownOption<string?>> DurchlaufStatusOptions =>
    [
        new("active", "En cours"),
        new("all", "Tous les cycles")
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> DurchlaufKlasseOptions =>
    [
        new(null, "Toutes les classes"),
        .. schoolKlassen
            .Where(klasse => klasse.CourseId.HasValue)
            .Select(klasse => new ModernDropdownOption<Guid?>((Guid?)klasse.CourseId, GetKlassenAnzeige(klasse)))
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> KlassenStufeFilterOptions =>
    [
        new(null, "Tous les noms de référence"),
        .. klassenStufen
            .OrderBy(stufe => stufe.Name)
            .Select(stufe => new ModernDropdownOption<Guid?>((Guid?)stufe.Id, stufe.Name))
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> StudentKlasseFilterOptionsModern =>
    [
        new(null, "Toutes les classes"),
        .. StudentKlassenFilterOptions.Select(klasse => new ModernDropdownOption<Guid?>((Guid?)klasse.CourseId, GetKlassenAnzeige(klasse)))
    ];

    private IReadOnlyList<StudentApprovalItem> StudentsWithoutActiveClass =>
        students
            .Where(student => string.IsNullOrWhiteSpace(schoolFilterName) || student.SchoolName == schoolFilterName)
            .Where(student => student.CurrentKlasseId is null)
            .ToList();

    private IReadOnlyList<StudentApprovalItem> FilteredStudentsWithoutActiveClass =>
        StudentsWithoutActiveClass
            .Where(student => reassignPreviousKlasseFilter == ReassignAllFilter
                || reassignPreviousKlasseFilter == ReassignNewFilter && !student.PreviousKlasseId.HasValue
                || Guid.TryParse(reassignPreviousKlasseFilter, out var previousKlasseId) && student.PreviousKlasseId == previousKlasseId)
            .Where(student => string.IsNullOrWhiteSpace(reassignSearch)
                             || TextMatch(student.FullName, reassignSearch)
                             || TextMatch(student.Email, reassignSearch)
                             || TextMatch(student.CurrentLevelCode, reassignSearch)
                             || TextMatch(student.CurrentKlasseName, reassignSearch)
                             || TextMatch(student.PreviousKlasseName, reassignSearch))
            .ToList();

    private const string ReassignAllFilter = "all";
    private const string ReassignNewFilter = "new";

    private IReadOnlyList<ModernDropdownOption<string>> ReassignPreviousKlasseOptions =>
    [
        new(ReassignAllFilter, "Tous les élèves sans classe"),
        new(ReassignNewFilter, "Nouveaux élèves (aucune classe précédente)"),
        .. StudentsWithoutActiveClass
            .Where(student => student.PreviousKlasseId.HasValue)
            .GroupBy(student => student.PreviousKlasseId!.Value)
            .OrderBy(group => group.First().PreviousKlasseName)
            .Select(group => new ModernDropdownOption<string>(group.Key.ToString(), $"Ancienne classe : {group.First().PreviousKlasseName}"))
    ];

    private int ReassignSelectedCount => reassignSelectedStudentIds.Count;

    private CourseFormModel? ReassignTargetKlasse => reassignTargetKlasseId.HasValue
        ? schoolKlassen.FirstOrDefault(klasse => klasse.CourseId == reassignTargetKlasseId.Value)
        : null;

    private IReadOnlyList<StudentApprovalItem> ReassignTargetStudents => reassignTargetKlasseId.HasValue
        ? students.Where(student => student.CurrentKlasseId == reassignTargetKlasseId.Value)
            .OrderBy(student => student.FullName)
            .ToList()
        : [];

    private int ArchiveSelectedCount => archiveSelectedStudentIds.Count;

    private IReadOnlyList<StudentApprovalItem> ArchivableFilteredStudents => FilteredStudents
        .Where(IsStudentArchivable)
        .ToList();

    private bool CanArchiveSelectedStudents => archiveSelectedStudentIds.Count > 0
        && !isDatabaseActionInProgress;

    private bool AreAllFilteredStudentsSelectedForArchive => ArchivableFilteredStudents.Count > 0
        && ArchivableFilteredStudents.All(student => archiveSelectedStudentIds.Contains(student.UserId));

    private bool CanSubmitReassign => reassignTargetKlasseId.HasValue
        && reassignSelectedStudentIds.Count > 0
        && !isDatabaseActionInProgress;

    private IReadOnlyList<ModernDropdownOption<Guid?>> ReassignKlasseOptions =>
    [
        new(null, "— Classe cible —"),
        .. schoolKlassen
            .Where(klasse => klasse.CourseId.HasValue)
            .OrderBy(klasse => klasse.ReferenceName)
            .ThenBy(klasse => klasse.Code)
            .Select(klasse => new ModernDropdownOption<Guid?>((Guid?)klasse.CourseId, GetKlassenAnzeige(klasse)))
    ];


    private IReadOnlyList<ModernDropdownOption<string?>> SchoolUserRoleOptions =>
        AllowedSchoolUserRoles
            .Select(role => new ModernDropdownOption<string?>(role, role switch
            {
                Rollen.Schueler => "Élève",
                Rollen.Lehrer => "Enseignant",
                Rollen.Koordinator => "Coordinateur",
                Rollen.SchulLeiter => "Directeur",
                _ => role
            }))
            .ToList();

    private string SchoolUsersCardTitle =>
        isProduktOwner
            ? "Directeurs des établissements"
            : isKoordinator
                ? "Enseignants de l'établissement"
                : "Équipe de l'établissement";

    private string SchoolUsersCardSubtitle =>
        isProduktOwner
            ? "Comptes de direction des établissements que vous suivez."
            : isKoordinator
                ? "Comptes des enseignants : classes attribuées, accès et blocage."
                : "Coordinateurs et enseignants : classes attribuées, droits et accès.";

    private string EmptySchoolUsersText =>
        isProduktOwner
            ? "Aucun directeur pour le moment."
            : isKoordinator
                ? "Aucun enseignant pour le moment."
                : "Aucun coordinateur ni enseignant pour le moment. Créez un compte avec « Nouvel utilisateur » ou importez une liste.";

    private string SchoolAssignmentHintText =>
        isProduktOwner
            ? "L'école est obligatoire pour créer un directeur."
            : isKoordinator
                ? "L'école est obligatoire pour créer un enseignant ou un élève."
                : "L'école est obligatoire pour créer un coordinateur, un enseignant ou un élève.";

    private IReadOnlyList<string> AllowedSchoolUserRoles =>
        isProduktOwner
            ? [Rollen.SchulLeiter]
            : isKoordinator
                ? [Rollen.Lehrer, Rollen.Schueler]
                : [Rollen.Koordinator, Rollen.Lehrer, Rollen.Schueler];

    private IReadOnlyList<ModernDropdownOption<Guid?>> UserSchoolOptions =>
    [
        new((Guid?)Guid.Empty, "Veuillez choisir"),
        .. schools.Select(school => new ModernDropdownOption<Guid?>((Guid?)school.SchoolId, school.SchoolName))
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> UserCurrentClassOptions =>
    [
        new(null, "À attribuer ultérieurement"),
        .. userFormKlassen
            .OrderBy(klasse => klasse.Code)
            .ThenBy(klasse => klasse.Title)
            .Select(klasse => new ModernDropdownOption<Guid?>((Guid?)klasse.CourseId, BuildUserCurrentClassLabel(klasse)))
    ];

    private static string BuildUserCurrentClassLabel(CourseFormModel klasse)
    {
        var code = klasse.Code?.Trim() ?? string.Empty;
        var title = klasse.Title?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(code))
        {
            return title;
        }

        if (string.IsNullOrWhiteSpace(title)
            || string.Equals(code, title, StringComparison.OrdinalIgnoreCase))
        {
            return code;
        }

        return $"{code} · {title}";
    }

    private IReadOnlyList<LehrerNiveauZuordnungItem> UserFormTeacherClassOptionsForSelectedSchools =>
        userFormTeacherClassOptions
            .Where(option => userForm.SchoolIds.Contains(option.SchulID))
            .ToList();

    private IReadOnlyList<LehrerNiveauZuordnungItem> FilteredUserFormTeacherClassOptions =>
        UserFormTeacherClassOptionsForSelectedSchools
            .Where(option => string.IsNullOrWhiteSpace(teacherClassReferenceFilter)
                             || string.Equals(option.ReferenceName, teacherClassReferenceFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private IReadOnlyList<ModernDropdownOption<string?>> TeacherClassReferenceFilterOptions =>
    [
        new(null, "Toutes les classes de référence"),
        .. UserFormTeacherClassOptionsForSelectedSchools
            .Select(option => option.ReferenceName)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(reference => reference)
            .Select(reference => new ModernDropdownOption<string?>(reference, reference!))
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> KlasseSchoolOptions =>
    [
        new(null, "-- Choisir une école --"),
        .. schools.Select(school => new ModernDropdownOption<Guid?>((Guid?)school.SchoolId, school.SchoolName, school.City))
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> KlassenStufeOptions =>
    [
        new(null, "-- Choisir une classe scolaire --"),
        .. klassenStufen.Select(stufe => new ModernDropdownOption<Guid?>((Guid?)stufe.Id, stufe.Name))
    ];

    private IReadOnlyList<ModernDropdownOption<ZahlungsArt?>> ZahlungsArtOptions =>
    [
        new(ZahlungsArt.Rate, "Échelonné"),
        new(ZahlungsArt.Cash, "Cash total")
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> VersetzungsZielKlasseOptions =>
    [
        new(null, "— Classe cible —"),
        .. schoolKlassen
            .Select(klasse => new ModernDropdownOption<Guid?>((Guid?)klasse.CourseId, GetKlassenAnzeige(klasse)))
    ];

    private IReadOnlyList<SchoolUserItem> FilteredSchoolUsers =>
        schoolFilterName is null
            ? schoolUsers
            : schoolUsers.Where(u => u.Schools.Contains(schoolFilterName)).ToList();

    private IReadOnlyList<SchoolUserItem> FilteredSchoolUsersOhneSchueler =>
        FilteredSchoolUsers
            .Where(user => user.RoleName != Rollen.Schueler)
            .ToList();

    private IReadOnlyList<SharedTeacherContentItem> FilteredSharedTeacherContent =>
        SelectedKlasseSchoolId is Guid schoolId
            ? sharedTeacherContent.Where(item => item.SchoolId == schoolId).ToList()
            : sharedTeacherContent;

    private void SetSchoolFilter(string? schoolName)
    {
        schoolFilterName = schoolName;
        studentKlassenStufeFilterId = null;
        studentKlasseFilterId = null;
        reassignSelectedStudentIds.Clear();
        archiveSelectedStudentIds.Clear();
        reassignTargetKlasseId = null;
    }

    private Task SetDurchlaufStatusFilterAsync(string? value)
    {
        durchlaufStatusFilter = value ?? "active";
        return Task.CompletedTask;
    }

    private Task SetReassignPreviousKlasseFilterAsync(string value)
    {
        reassignPreviousKlasseFilter = value;
        return Task.CompletedTask;
    }

    private Task SetDurchlaufKlasseFilterAsync(Guid? value)
    {
        durchlaufKlasseFilterId = value;
        return Task.CompletedTask;
    }

    private Task SetKlassenStufeFilterAsync(Guid? value)
    {
        klassenStufeFilterId = value;
        return Task.CompletedTask;
    }

    private async Task OpenNewStudentForKlasseAsync(CourseFormModel klasse)
    {
        await OpenNewUserModalAsync();
        userForm.RoleName = Rollen.Schueler;

        if (SelectedKlasseSchoolId is Guid schoolId)
        {
            SelectedStudentSchoolId = schoolId;
            await LoadUserFormKlassenAsync();
            userForm.CurrentKlasseId = klasse.CourseId;
        }
    }

    private Task SetStudentKlasseFilterAsync(Guid? value)
    {
        studentKlasseFilterId = value;
        archiveSelectedStudentIds.Clear();
        return Task.CompletedTask;
    }

    private Task SetStudentKlassenStufeFilterAsync(Guid? value)
    {
        studentKlassenStufeFilterId = value;

        if (studentKlasseFilterId.HasValue && !StudentKlassenFilterOptions.Any(klasse => klasse.CourseId == studentKlasseFilterId))
        {
            studentKlasseFilterId = null;
        }

        archiveSelectedStudentIds.Clear();
        return Task.CompletedTask;
    }

    private bool IsStudentSelectedForArchive(Guid studentId)
        => archiveSelectedStudentIds.Contains(studentId);

    private static bool IsStudentArchivable(StudentApprovalItem student)
        => !student.CurrentKlasseId.HasValue;

    private Task ToggleArchiveStudentAsync(Guid studentId, bool selected)
    {
        var student = students.FirstOrDefault(item => item.UserId == studentId);
        if (student is null || !IsStudentArchivable(student))
        {
            archiveSelectedStudentIds.Remove(studentId);
            return Task.CompletedTask;
        }

        if (selected)
        {
            archiveSelectedStudentIds.Add(studentId);
        }
        else
        {
            archiveSelectedStudentIds.Remove(studentId);
        }

        return Task.CompletedTask;
    }

    private Task ToggleSelectAllFilteredStudentsForArchiveAsync(bool selected)
    {
        if (selected)
        {
            foreach (var studentId in ArchivableFilteredStudents.Select(student => student.UserId))
            {
                archiveSelectedStudentIds.Add(studentId);
            }
        }
        else
        {
            archiveSelectedStudentIds.Clear();
        }

        return Task.CompletedTask;
    }

    private Task ArchiveSelectedStudentsAsync() => RunDatabaseActionAsync(ArchiveSelectedStudentsCoreAsync);

    private async Task ArchiveSelectedStudentsCoreAsync()
    {
        var selectedIds = archiveSelectedStudentIds
            .Where(studentId => ArchivableFilteredStudents.Any(student => student.UserId == studentId))
            .Distinct()
            .ToList();

        if (selectedIds.Count == 0)
        {
            SetResult(false, "Sélectionnez au moins un élève à archiver.");
            return;
        }

        var result = await DashboardService.ArchiveStudentsAsync(currentUserId, selectedIds, ComponentToken);
        SetResult(result.Succeeded, result.Message);
        if (result.Succeeded)
        {
            archiveSelectedStudentIds.Clear();
            expandedStudentId = null;
            levelEnrollments = [];
            await LoadAsync();
        }
    }

    private Task SetReassignTargetKlasseAsync(Guid? klasseId)
    {
        reassignTargetKlasseId = klasseId;
        return Task.CompletedTask;
    }

    private Task SetReassignSearchAsync(ChangeEventArgs args)
    {
        reassignSearch = args.Value?.ToString() ?? string.Empty;
        return Task.CompletedTask;
    }

    private bool IsStudentSelectedForReassign(Guid studentId)
        => reassignSelectedStudentIds.Contains(studentId);

    private Task ToggleReassignStudentAsync(Guid studentId, bool selected)
    {
        if (selected)
        {
            reassignSelectedStudentIds.Add(studentId);
        }
        else
        {
            reassignSelectedStudentIds.Remove(studentId);
        }

        return Task.CompletedTask;
    }

    private Task AssignSelectedStudentsToKlasseAsync() => RunDatabaseActionAsync(AssignSelectedStudentsToKlasseCoreAsync);

    private async Task AssignSelectedStudentsToKlasseCoreAsync()
    {
        if (SelectedKlasseSchoolId is null)
        {
            SetResult(false, "Sélectionnez d'abord une école.");
            return;
        }

        if (!reassignTargetKlasseId.HasValue)
        {
            SetResult(false, "Sélectionnez une classe cible.");
            return;
        }

        var selectedIds = reassignSelectedStudentIds
            .Where(studentId => FilteredStudentsWithoutActiveClass.Any(student => student.UserId == studentId))
            .ToList();
        if (selectedIds.Count == 0)
        {
            SetResult(false, "Sélectionnez au moins un élève sans classe active.");
            return;
        }

        var result = await DashboardService.AssignStudentsToKlasseAsync(
            currentUserId,
            reassignTargetKlasseId.Value,
            selectedIds,
            ComponentToken);

        SetResult(result.Succeeded, result.Message);
        if (result.Succeeded)
        {
            reassignSelectedStudentIds.Clear();
            reassignSearch = string.Empty;
            await LoadAsync();
        }
    }

    private static bool TextMatch(string? source, string term)
        => !string.IsNullOrWhiteSpace(source)
            && source.Contains(term, StringComparison.OrdinalIgnoreCase);

    private async Task SetSchoolUserRoleAsync(string? roleName)
    {
        userForm.RoleName = roleName ?? Rollen.Schueler;
        if (userForm.RoleName != Rollen.Koordinator)
        {
            userForm.CoordinatorFinanceAccesses.Clear();
            ResetCoordinatorFinanceDraft();
        }
        else if (!userForm.UserId.HasValue)
        {
            SetNewCoordinatorFinanceDefaults();
        }
        if (userForm.RoleName != Rollen.Lehrer)
        {
            userForm.FachIds.Clear();
        }

        if (userForm.RoleName == Rollen.Schueler)
        {
            await LoadUserFormKlassenAsync();
        }
        else
        {
            userFormKlassen = [];
        }

        teacherClassReferenceFilter = null;
        await LoadUserFormTeacherClassOptionsAsync();
        EnsureTeacherFachSelectionInUserForm();
    }

    private Task SetTeacherClassReferenceFilterAsync(string? referenceName)
    {
        teacherClassReferenceFilter = string.IsNullOrWhiteSpace(referenceName)
            ? null
            : referenceName;
        return Task.CompletedTask;
    }

    private async Task SetSelectedStudentSchoolAsync(Guid? schoolId)
    {
        SelectedStudentSchoolId = schoolId ?? Guid.Empty;
        await LoadUserFormKlassenAsync();
    }

    private Task SetUserCurrentClassAsync(Guid? classId)
    {
        userForm.CurrentKlasseId = classId;
        return Task.CompletedTask;
    }

    private async Task SetKlasseSchoolAsync(Guid? schoolId)
    {
        klasseForm.SchoolId = schoolId;
        await OnKlasseSchoolChangedAsync();
    }

    private Task SetKlassenStufeAsync(Guid? klassenStufeId)
    {
        klasseForm.KlassenStufeId = klassenStufeId;
        return Task.CompletedTask;
    }

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
            Key = "levelScoreDesc",
            Label = "Progression (élevée → basse)",
            KeySelector = s => s.CompletedLessonsCount,
            SecondaryKeySelector = s => s.FullName,
            Descending = true
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

    private Guid currentUserId;
    private Guid? expandedStudentId;


    private Guid? reassignTargetKlasseId;
    private string reassignSearch = string.Empty;
    private string reassignPreviousKlasseFilter = ReassignAllFilter;
    private HashSet<Guid> reassignSelectedStudentIds = [];
    private StudentApprovalItem? reassignStudentToRemove;
    private bool reassignRemoveConfirmOpen;
    private string? reassignRemoveErrorMessage;

    private void OpenReassignRemoveConfirmation(StudentApprovalItem student)
    {
        reassignStudentToRemove = student;
        reassignRemoveErrorMessage = null;
        reassignRemoveConfirmOpen = true;
    }

    private void CloseReassignRemoveConfirmation()
    {
        reassignRemoveConfirmOpen = false;
        reassignStudentToRemove = null;
        reassignRemoveErrorMessage = null;
    }

    private Task RemoveReassignedStudentAsync() => RunDatabaseActionAsync(RemoveReassignedStudentCoreAsync);

    private async Task RemoveReassignedStudentCoreAsync()
    {
        if (reassignStudentToRemove is null || !reassignTargetKlasseId.HasValue)
        {
            return;
        }

        var result = await DashboardService.RemoveStudentFromKlasseAsync(
            currentUserId,
            reassignStudentToRemove.UserId,
            reassignTargetKlasseId.Value,
            ComponentToken);
        if (result.Succeeded)
        {
            SetResult(true, result.Message);
            CloseReassignRemoveConfirmation();
            await LoadAsync();
            return;
        }

        reassignRemoveErrorMessage = result.Message;
    }
    private HashSet<Guid> archiveSelectedStudentIds = [];

    private string? message;
    private string? errorMessage;
    private bool isDatabaseActionInProgress;
    private bool isProduktOwner;
    private bool isKoordinator;

    [Inject]
    private DatabaseActionGuard DatabaseActionGuard { get; set; } = default!;

    private Guid pendingStudentId;
    private Guid pendingLevelId;
    private string pendingLevelCode = string.Empty;
    private bool aboModalOpen;
    private AboTyp? pendingAboTyp;
    private string pendingAboStudentName = string.Empty;
    private decimal pendingSubscriptionPrice;

    private decimal? PendingAboPreis => pendingAboTyp is null ? null : pendingSubscriptionPrice;

    private bool userModalOpen;
    private UserFormModel userForm = new();
    private bool coordinatorFinanceMenu;
    private bool coordinatorFinanceDashboard;
    private bool coordinatorFinanceEntries;
    private bool coordinatorFinanceCycles;
    private bool coordinatorFinanceBilling;
    private bool coordinatorFinanceHadDifferentAccess;

    private void OnCoordinatorFinanceMenuChanged()
    {
        if (coordinatorFinanceMenu) return;

        coordinatorFinanceDashboard = false;
        coordinatorFinanceEntries = false;
        coordinatorFinanceCycles = false;
        coordinatorFinanceBilling = false;
    }

    private void ResetCoordinatorFinanceDraft()
    {
        coordinatorFinanceMenu = false;
        coordinatorFinanceDashboard = false;
        coordinatorFinanceEntries = false;
        coordinatorFinanceCycles = false;
        coordinatorFinanceBilling = false;
        coordinatorFinanceHadDifferentAccess = false;
    }

    private void SetNewCoordinatorFinanceDefaults()
    {
        coordinatorFinanceMenu = true;
        coordinatorFinanceDashboard = false;
        coordinatorFinanceEntries = true;
        coordinatorFinanceCycles = true;
        coordinatorFinanceBilling = true;
    }

    private string? userStatusMessage;
    private string? userErrorMessage;
    private bool isStudentPersonalEditMode;
    private SchoolUserImportPreviewResult? teacherImportPreview;
    private SchoolUserImportPreviewResult? studentImportPreview;
    private string? teacherImportCsv;
    private string? studentImportCsv;

    private bool schoolModalOpen;
    private SchoolFormModel schoolForm = new() { Country = "Togo" };
    private string? schoolErrorMessage;

    private IReadOnlyList<ModernDropdownOption<string?>> SchoolCountryOptions
        => CountryOptions.WithCurrentValue(schoolForm.Country);

    private Task SetSchoolCountryAsync(string? country)
    {
        schoolForm.Country = country ?? string.Empty;
        return Task.CompletedTask;
    }

    // Niveau-Zuordnung für Lehrer
    private bool zuordnungModalOpen;
    private Guid zuordnungLehrerId;
    private string zuordnungLehrerName = string.Empty;
    private List<LehrerNiveauZuordnungItem> zuordnungItems = [];
    private List<FachOptionItem> schoolFachOptions = [];
    private string fachSuchbegriff = string.Empty;
    private string neueSchulFachBezeichnung = string.Empty;
    private string? fachVerwaltungStatusMessage;
    private string? fachVerwaltungErrorMessage;
    private bool fachDeleteConfirmOpen;
    private Guid? fachToDeleteId;
    private string fachToDeleteName = string.Empty;

    private IReadOnlyList<FachOptionItem> GefilterteSchoolFachOptions
        => SchoolFachOptionsForSelectedSchool
            .Where(option => string.IsNullOrWhiteSpace(fachSuchbegriff)
                || option.Name.Contains(fachSuchbegriff.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
    private IReadOnlyList<SharedTeacherContentItem> sharedTeacherContent = [];
    private bool canViewSharedTeacherContent;

    private TeacherSchoolOverview? SelectedSchoolForManagement
        => schools.FirstOrDefault(school => school.SchoolName == schoolFilterName)
           ?? (schools.Count == 1 ? schools[0] : null);

    private string schoolModalTitle => schoolForm.Id.HasValue ? "Modifier l'école" : "Nouvelle école";
    private string schoolModalSubmitLabel => schoolForm.Id.HasValue ? "Enregistrer l'école" : "Créer l'école";
    private bool schoolNameAlreadyExists
    {
        get
        {
            var normalizedName = schoolForm.Name?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return false;
            }

            return schools.Any(school =>
                school.SchoolId != schoolForm.Id
                && string.Equals(school.SchoolName?.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private string userModalTitle
        => isStudentPersonalEditMode
            ? "Modifier les données personnelles"
            : userForm.UserId.HasValue ? "Modifier l'utilisateur" : "Nouvel utilisateur";

    protected override async Task OnInitializedAsync()
    {
        if (!string.IsNullOrWhiteSpace(Onglet) && DirectorTabs.Contains(Onglet.Trim().ToLowerInvariant()))
        {
            activeDirectorTab = Onglet.Trim().ToLowerInvariant();
        }

        await LoadAsync();
    }

    private Task OpenEditSelectedSchoolAsync() => RunDatabaseActionAsync(OpenEditSelectedSchoolCoreAsync);

    private Task OpenEditSchoolAsync(Guid schoolId) => RunDatabaseActionAsync(() => OpenEditSchoolCoreAsync(schoolId));

    private async Task OpenEditSchoolCoreAsync(Guid schoolId)
    {
        schoolErrorMessage = null;
        var loaded = await DashboardService.GetSchoolForEditAsync(schoolId, ComponentToken);
        if (loaded is null)
        {
            schoolErrorMessage = "Impossible de charger l'école.";
            return;
        }

        schoolForm = loaded;
        schoolModalOpen = true;
    }

    private async Task OpenEditSelectedSchoolCoreAsync()
    {
        schoolErrorMessage = null;
        var school = SelectedSchoolForManagement;
        if (school is null)
        {
            schoolErrorMessage = "Sélectionnez d'abord une école.";
            return;
        }

        await OpenEditSchoolCoreAsync(school.SchoolId);
    }

    private Task SaveOwnSchoolAsync() => RunDatabaseActionAsync(SaveOwnSchoolCoreAsync);

    private async Task SaveOwnSchoolCoreAsync()
    {
        schoolErrorMessage = null;
        var result = schoolForm.Id.HasValue
            ? await DashboardService.UpdateSchoolForSchoolLeaderAsync(currentUserId, schoolForm, ComponentToken)
            : await DashboardService.CreateSchoolForSchoolLeaderAsync(currentUserId, schoolForm, ComponentToken);
        if (!result.Succeeded)
        {
            schoolErrorMessage = result.Message;
            return;
        }

        schoolModalOpen = false;
        message = result.Message;
        errorMessage = null;
        await LoadAsync();
    }

    private Task DeletePendingStudentAsync(Guid studentId) => RunDatabaseActionAsync(async () =>
    {
        var vorher = await KontoBenachrichtigungService.LadeEmpfaengerAsync(studentId, ComponentToken);
        var result = await DashboardService.DeletePendingSelfRegisteredStudentAsync(currentUserId, studentId, ComponentToken);
        SetResult(result.Succeeded, result.Message);
        if (result.Succeeded)
        {
            await KontoBenachrichtigungService.RegistrierungAbgelehntAsync(vorher, ComponentToken);
            await LoadAsync();
        }
    });

    private async Task LoadAsync()
    {
        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        isProduktOwner = authState.User.IsInRole(Rollen.ProduktOwner) && !authState.User.IsInRole(Rollen.SuperAdmin);
        isKoordinator = authState.User.IsInRole(Rollen.Koordinator) && !authState.User.IsInRole(Rollen.SchulLeiter);
        canViewSharedTeacherContent = authState.User.IsInRole(Rollen.SchulLeiter)
            || authState.User.IsInRole(Rollen.Koordinator);

        if (!canViewSharedTeacherContent && IsDirectorTabActive(DirectorTabShared))
        {
            activeDirectorTab = DirectorTabStudents;
        }

        var userIdValue = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            schools = [];
            students = [];
            schoolUsers = [];
            return;
        }

        var token = ComponentToken;
        try
        {
            currentUserId = userId;
            schools = await DashboardService.GetSchoolLeaderSchoolsAsync(userId, token);
            students = await DashboardService.GetSchoolLeaderStudentsAsync(userId, token);
            archiveSelectedStudentIds.IntersectWith(students.Where(IsStudentArchivable).Select(student => student.UserId).ToHashSet());
            schoolUsers = await DashboardService.GetSchoolUsersAsync(userId, token);
            sharedTeacherContent = canViewSharedTeacherContent
                ? await LessonService.GetSharedContentForSchoolLeaderAsync(userId, token)
                : [];
            await LoadSchoolKlassenAsync();
            await LoadSchoolFachOptionsAsync();
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private Task SetZahlungsArtAsync(ZahlungsArt? value)
    {
        if (value is ZahlungsArt art)
        {
            zahlungsArt = art;
            OnZahlungsArtChanged();
        }

        return Task.CompletedTask;
    }

    private Task SetZahlungWirkungsdatumAsync(DateTime? value)
    {
        if (value is DateTime datum)
        {
            zahlungWirkungsdatum = datum.Date;
        }

        return Task.CompletedTask;
    }

    private Task SetDurchlaufStartDatumAsync(DateTime? value)
    {
        if (value is DateTime datum)
        {
            durchlaufStartDatum = datum.Date;
        }

        return Task.CompletedTask;
    }

    private Task SetDurchlaufEndDatumAsync(DateTime? value)
    {
        if (value is DateTime datum)
        {
            durchlaufEndDatum = datum.Date;
        }

        return Task.CompletedTask;
    }

    private async Task SelectSchoolFilterAsync(string? schoolName)
    {
        SetSchoolFilter(schoolName);
        ResetImportState();
        await LoadSchoolKlassenAsync();
        await LoadSchoolFachOptionsAsync();
    }

    private void ResetImportState()
    {
        teacherImportPreview = null;
        studentImportPreview = null;
        teacherImportCsv = null;
        studentImportCsv = null;
    }

    private async Task LoadSchoolFachOptionsAsync()
    {
        var filteredSchoolId = ActiveFachSchoolId;

        schoolFachOptions = (await DashboardService.GetSchoolFachOptionsForDirectionAsync(currentUserId, filteredSchoolId, ComponentToken)).ToList();

        EnsureTeacherFachSelectionInUserForm();
    }

    private Task AddSchoolFachAsync() => RunDatabaseActionAsync(AddSchoolFachCoreAsync);

    private async Task AddSchoolFachCoreAsync()
    {
        fachVerwaltungErrorMessage = null;
        fachVerwaltungStatusMessage = null;

        if (ActiveFachSchoolId is not Guid schoolId)
        {
            fachVerwaltungErrorMessage = "Sélectionnez d'abord une école en haut de la page.";
            return;
        }

        var result = await DashboardService.CreateFachForSchoolByDirectionAsync(currentUserId, schoolId, neueSchulFachBezeichnung, ComponentToken);
        if (!result.Succeeded)
        {
            fachVerwaltungErrorMessage = result.Message;
            return;
        }

        neueSchulFachBezeichnung = string.Empty;
        fachVerwaltungStatusMessage = result.Message;
        await LoadSchoolFachOptionsAsync();
    }

    private static readonly IReadOnlyList<ModernDropdownOption<string?>> FachGruppenOptionen =
        [new("", "Sans groupe"), .. DashboardService.BulletinFachGruppen.Select(g => new ModernDropdownOption<string?>(g, g))];

    /// <summary>Bulletin-Gruppe eines Fachs ändern (Bilan lettres / sciences / autres).</summary>
    private async Task FachGruppeAsync(FachOptionItem fach, string? gruppe)
    {
        var result = await DashboardService.SetFachGruppeForDirectionAsync(currentUserId, fach.FachId, gruppe, ComponentToken);
        if (result.Succeeded)
        {
            fach.Gruppe = string.IsNullOrWhiteSpace(gruppe) ? null : gruppe;
            message = result.Message;
        }
        else
        {
            errorMessage = result.Message;
        }
    }

    private void AskDeleteSchoolFach(FachOptionItem fach)
    {
        fachVerwaltungErrorMessage = null;
        fachVerwaltungStatusMessage = null;

        if (fach.IstLehrerZugewiesen)
        {
            fachVerwaltungErrorMessage = "Cette matière est déjà attribuée à un enseignant et ne peut pas être supprimée.";
            return;
        }

        fachToDeleteId = fach.FachId;
        fachToDeleteName = fach.Name;
        fachDeleteConfirmOpen = true;
    }

    private void OnFachDeleteConfirmOpenChanged(bool open)
    {
        fachDeleteConfirmOpen = open;
        if (!open)
        {
            fachToDeleteId = null;
            fachToDeleteName = string.Empty;
        }
    }

    private Task ConfirmDeleteSchoolFachAsync() => RunDatabaseActionAsync(ConfirmDeleteSchoolFachCoreAsync);

    private async Task ConfirmDeleteSchoolFachCoreAsync()
    {
        if (fachToDeleteId is not Guid fachId)
        {
            fachDeleteConfirmOpen = false;
            return;
        }

        var result = await DashboardService.DeleteSchoolFachForDirectionAsync(currentUserId, fachId, ComponentToken);
        fachDeleteConfirmOpen = false;

        if (!result.Succeeded)
        {
            fachVerwaltungErrorMessage = result.Message;
            return;
        }

        fachVerwaltungStatusMessage = result.Message;
        fachVerwaltungErrorMessage = null;
        await LoadSchoolFachOptionsAsync();
    }

    private Task OnTeacherImportFileSelectedAsync(InputFileChangeEventArgs args)
        => PreviewImportFileAsync(args.File, SchoolUserImportKind.Teacher);

    private Task OnStudentImportFileSelectedAsync(InputFileChangeEventArgs args)
        => PreviewImportFileAsync(args.File, SchoolUserImportKind.Student);

    private async Task PreviewImportFileAsync(IBrowserFile file, SchoolUserImportKind kind)
    {
        if (SelectedKlasseSchoolId is not Guid schoolId)
        {
            SetResult(false, "Sélectionnez d'abord une école précise pour l'import.");
            return;
        }

        if (!string.Equals(Path.GetExtension(file.Name), ".csv", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Path.GetExtension(file.Name), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            SetResult(false, "Utilisez un fichier CSV ou Excel (.xlsx).");
            return;
        }

        if (kind == SchoolUserImportKind.Teacher) { teacherImportCsv = null; teacherImportPreview = null; }
        else { studentImportCsv = null; studentImportPreview = null; }

        try
        {
            await using var stream = file.OpenReadStream(maxAllowedSize: 2 * 1024 * 1024, cancellationToken: ComponentToken);
            string csv;
            if (string.Equals(Path.GetExtension(file.Name), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, ComponentToken);
                memory.Position = 0;
                csv = ExcelImportConverter.ConvertToCsv(memory);
            }
            else
            {
                csv = await ReadCsvFileAsync(stream, ComponentToken);
            }
            var preview = await SchoolUserImportService.PreviewAsync(new SchoolUserImportPreviewRequest
            {
                DirectorId = currentUserId,
                SchoolId = schoolId,
                Kind = kind,
                CsvContent = csv
            }, ComponentToken);

            if (kind == SchoolUserImportKind.Teacher)
            {
                teacherImportCsv = csv;
                teacherImportPreview = preview;
            }
            else
            {
                studentImportCsv = csv;
                studentImportPreview = preview;
            }

            SetResult(preview.Succeeded, preview.Succeeded
                ? $"Prévisualisation prête: {preview.ValidRowCount} ligne(s) valide(s)."
                : "La prévisualisation contient des erreurs. Corrigez le fichier puis rechargez-le.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Xml.XmlException)
        {
            Logger.LogError(ex, "Importdatei für Schule {SchoolId} konnte nicht gelesen werden.", schoolId);
            SetResult(false, "Le fichier n'a pas pu être lu. Utilisez un fichier CSV ou Excel (.xlsx) de moins de 2 Mo.");
        }
    }

    private static async Task<string> ReadCsvFileAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    private Task CommitTeacherImportAsync() => CommitImportAsync(SchoolUserImportKind.Teacher);

    private Task CommitStudentImportAsync() => CommitImportAsync(SchoolUserImportKind.Student);

    private Task CommitImportAsync(SchoolUserImportKind kind) => RunDatabaseActionAsync(async () =>
    {
        if (SelectedKlasseSchoolId is not Guid schoolId)
        {
            SetResult(false, "Sélectionnez d'abord une école précise pour l'import.");
            return;
        }

        var csv = kind == SchoolUserImportKind.Teacher ? teacherImportCsv : studentImportCsv;
        if (string.IsNullOrWhiteSpace(csv))
        {
            SetResult(false, "Chargez d'abord un fichier CSV ou Excel et vérifiez la prévisualisation.");
            return;
        }

        var result = await SchoolUserImportService.CommitAsync(new SchoolUserImportCommitRequest
        {
            DirectorId = currentUserId,
            SchoolId = schoolId,
            Kind = kind,
            CsvContent = csv
        }, ComponentToken);

        SetResult(result.Succeeded, result.Errors.Count == 0 ? result.Message : $"{result.Message} {string.Join(" ", result.Errors.Take(3))}");
        if (result.Succeeded)
        {
            if (kind == SchoolUserImportKind.Teacher)
            {
                teacherImportCsv = null;
                teacherImportPreview = null;
            }
            else
            {
                studentImportCsv = null;
                studentImportPreview = null;
            }

            await LoadAsync();
        }
    });

    private Task SetBlockedAsync(Guid studentId, bool isBlocked) => RunDatabaseActionAsync(() => SetBlockedCoreAsync(studentId, isBlocked));

    private async Task SetBlockedCoreAsync(Guid studentId, bool isBlocked)
    {
        var vorher = !isBlocked ? await KontoBenachrichtigungService.LadeEmpfaengerAsync(studentId, ComponentToken) : null;
        var result = await DashboardService.SetSchoolUserBlockedStateAsync(currentUserId, studentId, isBlocked, ComponentToken);
        if (result.Succeeded)
        {
            await KontoBenachrichtigungService.RegistrierungFreigegebenAsync(vorher, ComponentToken);
            message = isBlocked ? "L'élève a été bloqué." : "L'élève a été validé.";
            errorMessage = null;
            await LoadAsync();
        }
        else
        {
            message = null;
            errorMessage = result.Message;
        }
    }

    private async Task ToggleLevelPanelAsync(Guid studentId)
    {
        if (expandedStudentId == studentId)
        {
            expandedStudentId = null;
            levelEnrollments = [];
            return;
        }

        expandedStudentId = studentId;
        var student = students.FirstOrDefault(item => item.UserId == studentId);
        levelEnrollments = student is null
            ? []
            : await DashboardService.GetStudentLevelEnrollmentsAsync(studentId);
    }

    private void OpenAboModal(StudentApprovalItem student, LevelEnrollmentItem level)
    {
        pendingStudentId = student.UserId;
        pendingLevelId = level.KlasseId;
        pendingLevelCode = level.CourseCode;
        pendingAboStudentName = student.FullName;
        pendingSubscriptionPrice = level.SubscriptionPrice;
        pendingAboTyp = level.AboTyp;
        aboModalOpen = true;
    }

    private void SelectAboTyp(AboTyp aboTyp) => pendingAboTyp = aboTyp;

    private Task ConfirmAboAsync() => RunDatabaseActionAsync(ConfirmAboCoreAsync);

    private async Task ConfirmAboCoreAsync()
    {
        if (pendingAboTyp is not AboTyp aboTyp)
        {
            errorMessage = "Sélectionnez un abonnement.";
            return;
        }

        aboModalOpen = false;
        await AssignAboCoreAsync(pendingStudentId, pendingLevelId, aboTyp);
    }

    private async Task AssignAboCoreAsync(Guid studentId, Guid klasseId, AboTyp aboTyp)
    {
        var warVorherBezahlt = await KontoBenachrichtigungService.IstAboBezahltAsync(studentId, klasseId);
        var (succeeded, resultMessage) = await AboService.AboZuweisenAsync(studentId, klasseId, aboTyp, currentUserId);
        if (succeeded)
            await KontoBenachrichtigungService.AboPaiementEnregistreAsync(studentId, klasseId, warVorherBezahlt);
        SetResult(succeeded, resultMessage);
        await RefreshStudentAfterEnrollmentChangeAsync(studentId);
    }

    private async Task RefreshStudentAfterEnrollmentChangeAsync(Guid studentId)
    {
        await LoadAsync();
        expandedStudentId = studentId;
        levelEnrollments = await DashboardService.GetStudentLevelEnrollmentsAsync(studentId);
    }

    private void SetResult(bool succeeded, string resultMessage)
    {
        if (succeeded)
        {
            message = resultMessage;
            errorMessage = null;
        }
        else
        {
            errorMessage = resultMessage;
            message = null;
        }
    }

    private async Task OpenNewUserModalAsync()
    {
        isStudentPersonalEditMode = false;
        ResetCoordinatorFinanceDraft();
        userForm = new UserFormModel
        {
            RoleName = AllowedSchoolUserRoles.FirstOrDefault() ?? Rollen.Schueler,
            SchoolIds = []
        };

        if (schools.Count == 1)
        {
            userForm.SchoolIds = [schools[0].SchoolId];
        }

        await LoadUserFormKlassenAsync();

        if (userForm.RoleName == Rollen.Koordinator)
        {
            SetNewCoordinatorFinanceDefaults();
        }

        teacherClassReferenceFilter = null;
        userStatusMessage = null;
        userErrorMessage = null;
        userModalOpen = true;
    }

    private Guid SelectedStudentSchoolId
    {
        get => userForm.SchoolIds.FirstOrDefault();
        set
        {
            userForm.SchoolIds = value == Guid.Empty ? [] : [value];
            userForm.CurrentKlasseId = null;
        }
    }

    private async Task LoadUserFormKlassenAsync()
    {
        userFormKlassen = SelectedStudentSchoolId == Guid.Empty
            ? []
            : await DashboardService.GetSchoolKlassenAsync(SelectedStudentSchoolId, ComponentToken);
    }

    private async Task LoadUserFormTeacherClassOptionsAsync()
    {
        if (userForm.RoleName != Rollen.Lehrer)
        {
            userFormTeacherClassOptions = [];
            return;
        }

        var options = new List<LehrerNiveauZuordnungItem>();
        foreach (var school in schools)
        {
            var klassen = await DashboardService.GetSchoolKlassenAsync(school.SchoolId, ComponentToken);
            options.AddRange(klassen
                .Where(klasse => klasse.CourseId.HasValue)
                .Select(klasse => new LehrerNiveauZuordnungItem
                {
                    SchulID = school.SchoolId,
                    SchulName = school.SchoolName,
                    KlasseId = klasse.CourseId!.Value,
                    ReferenceName = klasse.ReferenceName,
                    NiveauCode = klasse.Code,
                    NiveauTitel = klasse.Title
                }));
        }

        userFormTeacherClassOptions = options;
        EnsureTeacherClassReferenceFilter();
    }

    private bool IsTeacherClassAssigned(Guid schoolId, Guid klasseId) => userForm.TeacherClassAssignments
        .Any(assignment => assignment.SchoolId == schoolId && assignment.KlasseId == klasseId);

    private void SetTeacherClassAssignment(LehrerNiveauZuordnungItem option, bool isAssigned)
    {
        userForm.TeacherClassAssignments.RemoveAll(assignment => assignment.SchoolId == option.SchulID && assignment.KlasseId == option.KlasseId);
        if (isAssigned)
        {
            userForm.TeacherClassAssignments.Add(new TeacherClassAssignmentFormModel
            {
                SchoolId = option.SchulID,
                KlasseId = option.KlasseId
            });
        }
    }

    private async Task OpenEditUserAsync(Guid userId)
    {
        var model = await DashboardService.GetSchoolUserForEditAsync(currentUserId, userId);
        if (model is null)
        {
            userErrorMessage = "Impossible de charger l'utilisateur.";
            return;
        }

        isStudentPersonalEditMode = model.RoleName == Rollen.Schueler;
        userForm = model;
        ResetCoordinatorFinanceDraft();
        if (!isKoordinator && model.RoleName == Rollen.Koordinator)
        {
            var accesses = model.CoordinatorFinanceAccesses;
            coordinatorFinanceHadDifferentAccess = accesses.Select(access => (access.Menu, access.Dashboard, access.Entries, access.Cycles, access.Billing))
                .Distinct().Count() > 1;
            coordinatorFinanceMenu = accesses.Count == model.SchoolIds.Count && accesses.All(access => access.Menu);
            coordinatorFinanceDashboard = coordinatorFinanceMenu && accesses.All(access => access.Dashboard);
            coordinatorFinanceEntries = coordinatorFinanceMenu && accesses.All(access => access.Entries);
            coordinatorFinanceCycles = coordinatorFinanceMenu && accesses.All(access => access.Cycles);
            coordinatorFinanceBilling = coordinatorFinanceMenu && accesses.All(access => access.Billing);
        }
        teacherClassReferenceFilter = null;
        await LoadUserFormKlassenAsync();
        await LoadUserFormTeacherClassOptionsAsync();
        EnsureTeacherFachSelectionInUserForm();
        userStatusMessage = null;
        userErrorMessage = null;
        userModalOpen = true;
    }

    private async Task OpenEditStudentPersonalAsync(Guid studentId)
    {
        var model = await DashboardService.GetSchoolUserForEditAsync(currentUserId, studentId, ComponentToken);
        if (model is null)
        {
            userErrorMessage = "Impossible de charger l'élève.";
            return;
        }

        if (model.RoleName != Rollen.Schueler)
        {
            userErrorMessage = "Seules les données personnelles d'un élève peuvent être modifiées ici.";
            return;
        }

        isStudentPersonalEditMode = true;
        userForm = model;
        teacherClassReferenceFilter = null;
        await LoadUserFormKlassenAsync();
        userStatusMessage = null;
        userErrorMessage = null;
        userModalOpen = true;
    }

    private void OnUserModalOpenChanged(bool open)
    {
        userModalOpen = open;
        if (!open)
        {
            isStudentPersonalEditMode = false;
            ResetCoordinatorFinanceDraft();
            userForm = new UserFormModel();
            userFormKlassen = [];
            userFormTeacherClassOptions = [];
            teacherClassReferenceFilter = null;
        }
    }

    private bool IsTeacherFachAssigned(Guid fachId)
        => userForm.FachIds.Contains(fachId);

    private void SetTeacherFachAssignment(Guid fachId, bool isAssigned)
    {
        if (isAssigned)
        {
            if (!userForm.FachIds.Contains(fachId))
            {
                userForm.FachIds.Add(fachId);
            }

            return;
        }

        userForm.FachIds.Remove(fachId);
    }

    private void EnsureTeacherFachSelectionInUserForm()
    {
        if (userForm.RoleName != Rollen.Lehrer)
        {
            userForm.FachIds.Clear();
            return;
        }

        var allowedFachIds = schoolFachOptions
            .Where(option => userForm.SchoolIds.Contains(option.SchoolId))
            .Select(option => option.FachId)
            .ToHashSet();

        userForm.FachIds = userForm.FachIds
            .Where(fachId => allowedFachIds.Contains(fachId))
            .Distinct()
            .ToList();
    }

    private void EnsureTeacherClassReferenceFilter()
    {
        if (string.IsNullOrWhiteSpace(teacherClassReferenceFilter))
        {
            return;
        }

        var stillAvailable = UserFormTeacherClassOptionsForSelectedSchools
            .Any(option => string.Equals(option.ReferenceName, teacherClassReferenceFilter, StringComparison.OrdinalIgnoreCase));

        if (!stillAvailable)
        {
            teacherClassReferenceFilter = null;
        }
    }

    private Task SaveUserAsync() => RunDatabaseActionAsync(SaveUserCoreAsync);
    private List<SchoolUserImportZugang> neueZugangsdaten = [];
    private string zugangsdatenTitel = "Identifiants initiaux";

    // Neues Passwort durch die Direktion (Schüler/Lehrkräfte ohne E-Mail)
    [Inject] private KontoZugangService KontoZugang { get; set; } = default!;
    private (Guid Id, string Name)? passwortZiel;

    private void ZugangsdatenSchliessen()
    {
        neueZugangsdaten.Clear();
        zugangsdatenTitel = "Identifiants initiaux";
    }
    private string? passwortFehler;

    private void PasswortFragen(Guid benutzerId, string name)
    {
        passwortFehler = null;
        passwortZiel = (benutzerId, name);
    }

    private async Task NeuesPasswortAsync()
    {
        if (passwortZiel is not { } ziel || isDatabaseActionInProgress) return;
        isDatabaseActionInProgress = true;
        try
        {
            var zugang = await KontoZugang.NeuesPasswortAsync(currentUserId, ziel.Id, ComponentToken);
            passwortZiel = null;
            zugangsdatenTitel = $"Nouveau mot de passe – {ziel.Name}";
            neueZugangsdaten = [zugang];
        }
        catch (InvalidOperationException ex)
        {
            passwortFehler = ex.Message;
        }
        finally
        {
            isDatabaseActionInProgress = false;
        }
    }

    private async Task SaveUserCoreAsync()
    {
        userErrorMessage = null;
        userStatusMessage = null;

        // Vor dem Speichern merken, ob es sich um eine Neuanlage handelt:
        // Nur dann wird anschliessend eine Willkommensmail versendet.
        var istNeuanlage = !userForm.UserId.HasValue;
        var email = userForm.Email?.Trim() ?? string.Empty;
        var vorname = userForm.Vorname?.Trim() ?? string.Empty;

        if (!isKoordinator && userForm.RoleName == Rollen.Koordinator)
        {
            var menu = coordinatorFinanceMenu && (coordinatorFinanceDashboard || coordinatorFinanceEntries || coordinatorFinanceCycles || coordinatorFinanceBilling);
            userForm.CoordinatorFinanceAccesses = userForm.SchoolIds.Select(schoolId =>
                new CoordinatorFinanceAccessFormModel
                {
                    SchoolId = schoolId,
                    Menu = menu,
                    Dashboard = menu && coordinatorFinanceDashboard,
                    Entries = menu && coordinatorFinanceEntries,
                    Cycles = menu && coordinatorFinanceCycles,
                    Billing = menu && coordinatorFinanceBilling
                }).ToList();
        }

        var modelToSave = userForm;
        if (isStudentPersonalEditMode)
        {
            if (!userForm.UserId.HasValue)
            {
                userErrorMessage = "La modification des données personnelles nécessite un élève existant.";
                return;
            }

            var existing = await DashboardService.GetSchoolUserForEditAsync(currentUserId, userForm.UserId.Value, ComponentToken);
            if (existing is null || existing.RoleName != Rollen.Schueler)
            {
                userErrorMessage = "Cet élève n'est plus accessible pour modification.";
                return;
            }

            modelToSave = new UserFormModel
            {
                UserId = existing.UserId,
                Vorname = vorname,
                Nachname = userForm.Nachname,
                Identifiant = userForm.Identifiant,
                Email = email,
                TelefonNummer = userForm.TelefonNummer,
                ElternTelefonNummer = userForm.ElternTelefonNummer,
                ElternEmail = userForm.ElternEmail,
                RoleName = existing.RoleName,
                IsBlocked = existing.IsBlocked,
                IsSelfRegistered = existing.IsSelfRegistered,
                SchoolIds = existing.SchoolIds.ToList(),
                FachIds = existing.FachIds.ToList(),
                CurrentKlasseId = existing.CurrentKlasseId,
                TeacherClassAssignments = existing.TeacherClassAssignments
                    .Select(assignment => new TeacherClassAssignmentFormModel
                    {
                        SchoolId = assignment.SchoolId,
                        KlasseId = assignment.KlasseId,
                        Wochenstunden = assignment.Wochenstunden
                    })
                    .ToList()
            };
        }

        var vorher = modelToSave.UserId is Guid userId
            ? await KontoBenachrichtigungService.LadeEmpfaengerAsync(userId, ComponentToken)
            : null;
        var result = await DashboardService.SaveSchoolUserAsync(currentUserId, modelToSave, ComponentToken);
        if (!result.Succeeded)
        {
            userErrorMessage = result.Message;
            return;
        }

        await KontoBenachrichtigungService.AdresseModifieeAsync(vorher, ComponentToken);

        if (istNeuanlage && !string.IsNullOrWhiteSpace(modelToSave.Password))
            neueZugangsdaten = [new SchoolUserImportZugang(modelToSave.Identifiant, modelToSave.Password)];

        if (istNeuanlage)
        {
            // Best-Effort: Ein fehlgeschlagener Versand (z. B. nicht existierende
            // Adresse) darf die Benutzeranlage nicht beeintraechtigen.
            await AuthService.SendWelcomeEmailAsync(modelToSave.Identifiant);
        }

        userStatusMessage = result.Message;
        userModalOpen = false;
        isStudentPersonalEditMode = false;
        ResetCoordinatorFinanceDraft();
        userForm = new UserFormModel();
        await LoadAsync();
    }

    private Task SetUserBlockedAsync(Guid userId, bool isBlocked) => RunDatabaseActionAsync(() => SetUserBlockedCoreAsync(userId, isBlocked));

    private async Task SetUserBlockedCoreAsync(Guid userId, bool isBlocked)
    {
        userErrorMessage = null;
        userStatusMessage = null;

        var vorher = !isBlocked ? await KontoBenachrichtigungService.LadeEmpfaengerAsync(userId, ComponentToken) : null;
        var result = await DashboardService.SetSchoolUserBlockedStateAsync(currentUserId, userId, isBlocked, ComponentToken);
        if (result.Succeeded)
        {
            await KontoBenachrichtigungService.RegistrierungFreigegebenAsync(vorher, ComponentToken);
            userStatusMessage = result.Message;
            await LoadAsync();
        }
        else
        {
            userErrorMessage = result.Message;
        }
    }

    private async Task OpenZuordnungAsync(Guid teacherId, string teacherName)
    {
        zuordnungLehrerId = teacherId;
        zuordnungLehrerName = teacherName;
        zuordnungItems = (await DashboardService.GetLehrerNiveauZuordnungenAsync(currentUserId, teacherId)).ToList();
        zuordnungModalOpen = true;
    }


    private void OnZuordnungModalOpenChanged(bool open)
    {
        zuordnungModalOpen = open;
        if (!open)
        {
            zuordnungItems = [];
        }
    }


    private Task SaveZuordnungenAsync() => RunDatabaseActionAsync(SaveZuordnungenCoreAsync);

    private async Task SaveZuordnungenCoreAsync()
    {
        var ausgewaehlt = zuordnungItems
            .Where(z => z.IstZugeordnet)
            .Select(z => (z.SchulID, z.KlasseId, z.Wochenstunden))
            .ToList();

        await DashboardService.SetLehrerNiveauZuordnungenAsync(currentUserId, zuordnungLehrerId, ausgewaehlt);

        zuordnungModalOpen = false;
        zuordnungItems = [];
        message = ausgewaehlt.Count == 0
            ? "L'enseignant ne peut voir aucun élève tant qu'aucune classe ne lui est attribuée."
            : "Les classes attribuées à l'enseignant ont été enregistrées.";
        errorMessage = null;
        await LoadAsync();
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

    private static string GetRoleLabel(string roleName) => roleName switch
    {
        "Koordinator" => "Coordinateur",
        "Lehrer" => "Enseignant",
        "Schüler" => "Élève",
        _ => roleName
    };

    private static string GetStudentAboHeaderLabel(bool hasPaidEnrollment, bool isSkoulouPlus)
        => hasPaidEnrollment
            ? "Abo actuel : SKOULOU payé"
            : isSkoulouPlus
                ? "Abo actuel : SKOULOU actif (paiement en attente)"
                : "Abo actuel : aucun abonnement";

    private static string GetSchoolFeeHeaderLabel(decimal paidAmount, decimal? totalAmount, bool hasActiveCycle)
        => !hasActiveCycle || !totalAmount.HasValue
            ? "Scolarité : cycle finance à démarrer"
            : totalAmount.Value <= 0m
                ? "Scolarité : à définir"
                : $"Scolarité : {paidAmount.ToString("N0", Fr)} / {totalAmount.Value.ToString("N0", Fr)} FCFA payé";

    private static bool IsReferenceIncoherent(CourseFormModel klasse)
        => HasReferencePrefixMismatch(klasse.ReferenceName, klasse.Code, klasse.Title);

    private static bool HasReferencePrefixMismatch(string? referenceName, string? code, string? title)
    {
        var reference = NormalizeReferenceToken(referenceName);
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var codeToken = NormalizeReferenceToken(code);
        var titleToken = NormalizeReferenceToken(title);

        if (string.IsNullOrWhiteSpace(codeToken) && string.IsNullOrWhiteSpace(titleToken))
        {
            return false;
        }

        return !(codeToken.StartsWith(reference, StringComparison.Ordinal)
                 || titleToken.StartsWith(reference, StringComparison.Ordinal));
    }

    private static string NormalizeReferenceToken(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value
                .Where(char.IsLetterOrDigit)
                .ToArray())
                .ToUpperInvariant();

    private string? ResolveFormReferenceName()
    {
        if (!string.IsNullOrWhiteSpace(klasseForm.NeueKlassenStufe))
        {
            return klasseForm.NeueKlassenStufe;
        }

        if (klasseForm.KlassenStufeId is Guid stufeId)
        {
            return klassenStufen.FirstOrDefault(stufe => stufe.Id == stufeId)?.Name;
        }

        return null;
    }

    private IReadOnlyList<DataListChip<SchoolUserItem>> schoolUserChips => SchoolUserChipsAlle.Where(chip => chip.Count > 0).ToList();

    /// <summary>Schnellfilter im Reiter Élèves; nur Filter mit Treffern werden angezeigt.</summary>
    private IReadOnlyList<DataListChip<StudentApprovalItem>> studentChips =>
        new DataListChip<StudentApprovalItem>[]
        {
            new() { Key = "attente", Label = "À valider", Accent = "amber", Predicate = s => s.IsBlocked, Count = FilteredStudents.Count(s => s.IsBlocked) },
            new() { Key = "sansclasse", Label = "Sans classe", Accent = "rose", Predicate = s => !s.CurrentKlasseId.HasValue, Count = FilteredStudents.Count(s => !s.CurrentKlasseId.HasValue) },
            new() { Key = "scolarite", Label = "Scolarité incomplète", Accent = "amber", Predicate = s => s.HasActiveSchoolFeeCycle && s.SchoolFeeAmount > 0m && !s.IsSchoolFeeFullyPaid, Count = FilteredStudents.Count(s => s.HasActiveSchoolFeeCycle && s.SchoolFeeAmount > 0m && !s.IsSchoolFeeFullyPaid) },
        }.Where(chip => chip.Count > 0).ToList();

    private static readonly (int Nr, string Titel, string Text)[] ImportSchritte =
    [
        (1, "Ouvrez Excel", "Créez un nouveau tableau."),
        (2, "Copiez les titres", "Reprenez les titres de colonnes de l'exemple ci-dessous."),
        (3, "Une personne par ligne", "Les champs facultatifs peuvent rester vides ; l'identifiant est créé automatiquement."),
        (4, "Enregistrez et chargez", "Format CSV UTF-8 (*.csv) ou Excel (*.xlsx), puis choisissez le fichier."),
    ];

    /// <summary>Zweite Zeile einer Klassenkarte: Code, Referenzname und (falls gesetzt) Preis.</summary>
    private static string KlassenMeta(CourseFormModel klasse)
    {
        var teile = new List<string>();
        if (!string.IsNullOrWhiteSpace(klasse.Code) && klasse.Code != klasse.Title)
        {
            teile.Add($"Code {klasse.Code}");
        }

        if (!string.IsNullOrWhiteSpace(klasse.ReferenceName))
        {
            teile.Add($"Réf. {klasse.ReferenceName}");
        }

        if (klasse.Price > 0m)
        {
            teile.Add($"{klasse.Price.ToString("N0", Fr)} FCFA");
        }

        return teile.Count == 0 ? "Classe de l'établissement" : string.Join(" · ", teile);
    }

    private IReadOnlyList<DataListChip<SchoolUserItem>> SchoolUserChipsAlle =>
    [
        new()
        {
            Key = "Koordinator",
            Label = "Coordinateurs",
            Accent = "violet",
            Predicate = u => u.RoleName == "Koordinator",
            Count = schoolUsers.Count(u => u.RoleName == "Koordinator")
        },
        new()
        {
            Key = "Lehrer",
            Label = "Enseignants",
            Accent = "blue",
            Predicate = u => u.RoleName == "Lehrer",
            Count = schoolUsers.Count(u => u.RoleName == "Lehrer")
        },
    ];

    // ---------------------------------------------------------------------
    // Klassenverwaltung (schul-eigene Klassen)
    // ---------------------------------------------------------------------

    private IReadOnlyList<CourseFormModel> schoolKlassen = [];
    private bool darfCycleZahlungenVerwalten;

    private async Task<bool> PruefeCycleZahlungsrechtAsync()
    {
        if (SelectedKlasseSchoolId is not Guid schoolId)
            return false;

        darfCycleZahlungenVerwalten = (await FinanzService.GetFinanceAccessAsync(currentUserId, schoolId,
            cancellationToken: ComponentToken)).Cycles;
        if (!darfCycleZahlungenVerwalten)
            klasseErrorMessage = "L'accès « Paiements des cycles » est nécessaire pour cette action.";
        return darfCycleZahlungenVerwalten;
    }
    private IReadOnlyList<KlassenStufeViewModel> klassenStufen = [];
    private Dictionary<Guid, DurchlaufUebersicht> durchlaeufeNachKlasse = [];
    private IReadOnlyList<DurchlaufUebersicht> durchlaeufe = [];
    private string durchlaufStatusFilter = "active";
    private Guid? durchlaufKlasseFilterId;
    private Guid? klassenStufeFilterId;
    private KlassenFinanzbericht? klassenFinanzbericht;
    private Guid? klassenFinanzberichtDurchlaufId;
    private bool klasseModalOpen;
    private CourseFormModel klasseForm = new();
    private string? klasseStatusMessage;
    private string? klasseErrorMessage;
    private string? klasseWarnMessage;
    private bool autoAssignConfirmOpen;

    private bool durchlaufStartModalOpen;
    private bool durchlaufStartDurchUeberweisungGesperrt;
    private CourseFormModel? startKlasse;
    private decimal durchlaufPreis;
    private DateTime durchlaufStartDatum = DateTime.Today;
    private bool durchlaufEndeModalOpen;
    private DurchlaufUebersicht? zuBeendenderDurchlauf;
    private int beendenTeilnehmerGesamt;
    private int beendenOhneNote;
    private bool durchlaufEndeEndgueltigModalOpen;
    private DateTime durchlaufEndDatum = DateTime.Today;
    private bool abschlussNotenOpen;
    private Guid? abschlussNotenDurchlaufId;
    private void OpenAbschlussNoten(Guid durchlaufId)
    {
        abschlussNotenDurchlaufId = durchlaufId;
        abschlussNotenOpen = true;
    }
    private bool zahlungModalOpen;
    private DurchlaufUebersicht? zahlungDurchlauf;
    private Guid zahlungStudentId;
    private decimal zahlungBetrag;
    private ZahlungsArt zahlungsArt = ZahlungsArt.Rate;
    private DateTime zahlungWirkungsdatum = DateTime.Today;
    private string zahlungSchuelerSuche = string.Empty;

    private IReadOnlyList<SchuelerZahlungsstand> GefilterteZahlungsSchueler => zahlungDurchlauf?.Schueler
        .Where(s => string.IsNullOrWhiteSpace(zahlungSchuelerSuche)
                    || s.Name.Contains(zahlungSchuelerSuche, StringComparison.OrdinalIgnoreCase))
        .ToList() ?? [];

    private bool IstBarzahlung => zahlungsArt == ZahlungsArt.Cash;

    private IReadOnlyList<DurchlaufUebersicht> GefilterteDurchlaeufe => durchlaeufe
        .Where(d => durchlaufStatusFilter switch
        {
            "active" => d.IstAktiv,
            "closed" => !d.IstAktiv,
            _ => true
        })
        .Where(d => !klassenStufeFilterId.HasValue
                    || schoolKlassen.FirstOrDefault(klasse => klasse.CourseId == d.KlasseId)?.KlassenStufeId == klassenStufeFilterId)
        .ToList();

    private IReadOnlyList<CourseFormModel> GefilterteKlassen => schoolKlassen
        .Where(klasse => !klassenStufeFilterId.HasValue
                         || klasse.KlassenStufeId == klassenStufeFilterId)
        .ToList();

    private CourseFormModel? ExistingKlasseFromForm
        => klasseForm.CourseId is Guid klasseId
            ? schoolKlassen.FirstOrDefault(klasse => klasse.CourseId == klasseId)
            : null;

    private bool IsAktiverCycleForKlasseForm
        => ExistingKlasseFromForm?.CourseId is Guid klasseId
            && durchlaeufeNachKlasse.TryGetValue(klasseId, out var durchlauf)
            && durchlauf.IstAktiv;

    private bool IsAutoAssignActivationPendingConfirmation
        => ExistingKlasseFromForm is not null
            && !ExistingKlasseFromForm.AutoAssignSkoulouAppAboOnCycleStart
            && klasseForm.AutoAssignSkoulouAppAboOnCycleStart
            && IsAktiverCycleForKlasseForm;

    private static string GetKlassenAnzeige(CourseFormModel klasse)
        => string.IsNullOrWhiteSpace(klasse.ReferenceName)
            ? klasse.Title
            : $"{klasse.Title} · Réf. {klasse.ReferenceName}";

    private string GetTeacherClassAssignmentLabel(LehrerNiveauZuordnungItem option)
    {
        var code = option.NiveauCode?.Trim();
        if (!string.IsNullOrWhiteSpace(code))
        {
            return code;
        }

        if (!string.IsNullOrWhiteSpace(option.NiveauTitel))
        {
            return option.NiveauTitel.Trim();
        }

        return BuildClassCodeWithReference(option.NiveauCode, option.ReferenceName);
    }

    private string GetTeacherFachAssignmentLabel(FachOptionItem option)
        => option.Name;

    private static string BuildClassCodeWithReference(string? code, string? reference)
    {
        var normalizedCode = code?.Trim() ?? string.Empty;
        var normalizedReference = reference?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return string.IsNullOrWhiteSpace(normalizedReference)
                ? "Classe sans code"
                : $"Classe réf. {normalizedReference}";
        }

        if (string.IsNullOrWhiteSpace(normalizedReference)
            || normalizedCode.Contains(normalizedReference, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedCode;
        }

        return $"{normalizedCode} · Réf. {normalizedReference}";
    }

    private static int? GetRemainingPlaetze(CourseFormModel klasse)
        => klasse.MaxPlaetze.HasValue ? klasse.MaxPlaetze.Value - klasse.CurrentStudentCount : null;

    private static bool IsKlasseOverbooked(CourseFormModel klasse)
        => klasse.MaxPlaetze.HasValue && klasse.CurrentStudentCount > klasse.MaxPlaetze.Value;

    private static decimal GetOccupancyPercent(CourseFormModel klasse)
    {
        if (!klasse.MaxPlaetze.HasValue || klasse.MaxPlaetze.Value <= 0)
        {
            return 0m;
        }

        return Math.Round(Math.Min(200m, klasse.CurrentStudentCount * 100m / klasse.MaxPlaetze.Value), 0);
    }

    private static string GetOccupancyBarClass(CourseFormModel klasse)
    {
        if (!klasse.MaxPlaetze.HasValue)
        {
            return "bg-slate-400";
        }

        var percent = klasse.MaxPlaetze.Value <= 0 ? 0m : klasse.CurrentStudentCount * 100m / klasse.MaxPlaetze.Value;
        if (percent > 100m)
        {
            return "bg-rose-600";
        }

        if (percent >= 85m)
        {
            return "bg-amber-500";
        }

        return "bg-emerald-500";
    }

    private async Task ShowKlassenFinanzberichtAsync(DurchlaufUebersicht durchlauf)
    {
        klasseErrorMessage = null;
        klassenFinanzbericht = await FinanzService.GetKlassenFinanzberichtAsync(durchlauf.DurchlaufID, ComponentToken);
        if (klassenFinanzbericht is null)
        {
            klasseErrorMessage = "Le rapport financier du cycle est introuvable.";
            klassenFinanzberichtDurchlaufId = null;
            return;
        }

        klassenFinanzberichtDurchlaufId = durchlauf.DurchlaufID;
    }

    /// <summary>
    /// Die aktuell im Filter gewählte Schule (oder die einzige Schule des Schulleiters).
    /// </summary>
    private Guid? SelectedKlasseSchoolId =>
        schoolFilterName is null
            ? (schools.Count == 1 ? schools[0].SchoolId : (Guid?)null)
            : schools.FirstOrDefault(s => s.SchoolName == schoolFilterName)?.SchoolId;

    private TeacherSchoolOverview? SelectedKlasseSchool =>
        SelectedKlasseSchoolId is Guid id ? schools.FirstOrDefault(s => s.SchoolId == id) : null;

    private async Task OpenZahlungModalAsync(DurchlaufUebersicht durchlauf)
    {
        if (!await PruefeCycleZahlungsrechtAsync()) return;

        zahlungDurchlauf = await FinanzService.GetDurchlaufUebersichtAsync(durchlauf.DurchlaufID, ComponentToken);
        if (zahlungDurchlauf is null)
        {
            klasseErrorMessage = "Le cycle est introuvable.";
            return;
        }

        zahlungStudentId = Guid.Empty;
        zahlungsArt = ZahlungsArt.Rate;
        zahlungBetrag = zahlungDurchlauf.NiveauPreis;
        zahlungWirkungsdatum = DateTime.Today < zahlungDurchlauf.StartDatum ? zahlungDurchlauf.StartDatum : DateTime.Today;
        zahlungSchuelerSuche = string.Empty;
        zahlungModalOpen = true;
    }


    private void WaehleZahlungSchueler(Guid studentId)
    {
        zahlungStudentId = studentId;
        var offen = zahlungDurchlauf?.Schueler.FirstOrDefault(s => s.StudentId == studentId)?.Offen;
        if (offen.HasValue && !IstBarzahlung)
        {
            zahlungBetrag = offen.Value;
        }
    }

    private void OnZahlungsArtChanged()
    {
        if (IstBarzahlung && zahlungDurchlauf is not null)
        {
            zahlungBetrag = zahlungDurchlauf.NiveauPreis;
        }
    }

    /// <summary>Zuletzt erfasste Zahlung – Reçu direkt drucken.</summary>
    private Guid? letzteQuittungID;

    private Task ZahlungBuchenAsync() => RunDatabaseActionAsync(ZahlungBuchenCoreAsync);

    private async Task ZahlungBuchenCoreAsync()
    {
        if (!await PruefeCycleZahlungsrechtAsync()) return;

        if (zahlungDurchlauf is null || zahlungStudentId == Guid.Empty)
        {
            klasseErrorMessage = "Sélectionnez un élève.";
            return;
        }

        var result = await FinanzService.ZahlungBuchenAsync(
            zahlungDurchlauf.DurchlaufID,
            zahlungStudentId,
            zahlungBetrag,
            zahlungsArt,
            zahlungWirkungsdatum,
            currentUserId,
            istSuperAdmin: false,
            autorisierungErzwingen: true,
            cancellationToken: ComponentToken);
        if (!result.Succeeded)
        {
            klasseErrorMessage = result.ErrorMessage;
            return;
        }

        if (result.Zahlung is not null)
        {
            await KontoBenachrichtigungService.PaiementEnregistreAsync(result.Zahlung.Id, ComponentToken);
            letzteQuittungID = result.Zahlung.Id;
        }
        klasseStatusMessage = "Le paiement a été enregistré.";
        zahlungModalOpen = false;
        zahlungDurchlauf = null;
        await LoadSchoolKlassenAsync();
    }

    private async Task LoadSchoolKlassenAsync()
    {
        mentionEditId = null;
        mentionText = string.Empty;
        if (SelectedKlasseSchoolId is not Guid schoolId)
        {
            darfCycleZahlungenVerwalten = false;
            schulMentions = [];
            schoolKlassen = [];
            durchlaeufe = [];
            durchlaeufeNachKlasse = [];
            klassenFinanzbericht = null;
            klassenFinanzberichtDurchlaufId = null;
            return;
        }

        try
        {
            darfCycleZahlungenVerwalten = (await FinanzService.GetFinanceAccessAsync(currentUserId, schoolId,
                cancellationToken: ComponentToken)).Cycles;
            schoolKlassen = await DashboardService.GetSchoolKlassenAsync(schoolId, ComponentToken);
            schulMentions = await ExamService.GetAbschlussMentionsAsync(schoolId, currentUserId, ComponentToken);
            klassenStufen = await DashboardService.GetKlassenStufenAsync(schoolId, ComponentToken);
            durchlaeufe = await FinanzService.GetDurchlaeufeAsync(schoolId, ComponentToken);
            durchlaeufeNachKlasse = durchlaeufe
                .GroupBy(durchlauf => durchlauf.KlasseId)
                .ToDictionary(
                    gruppe => gruppe.Key,
                    gruppe => gruppe.OrderByDescending(d => d.IstAktiv).ThenByDescending(d => d.StartDatum).First());
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen.
        }
    }

    private void OpenNewKlasseModal()
    {
        klasseForm = new CourseFormModel
        {
            SchoolId = SelectedKlasseSchoolId,
            IstSystemNiveau = false,
            RecommendedLessonCount = 1,
            MaxPlaetze = null,
            AutoAssignSkoulouAppAboOnCycleStart = false
        };
        autoAssignConfirmOpen = false;
        klasseStatusMessage = null;
        klasseErrorMessage = null;
        klasseWarnMessage = null;
        klasseModalOpen = true;
    }

    private async Task OnKlasseSchoolChangedAsync()
    {
        klasseForm.KlassenStufeId = null;
        klasseForm.NeueKlassenStufe = null;
        klassenStufen = klasseForm.SchoolId is Guid schoolId
            ? await DashboardService.GetKlassenStufenAsync(schoolId, ComponentToken)
            : [];
    }

    private void OpenEditKlasseModal(CourseFormModel klasse)
    {
        if (klasse.IstSystemNiveau)
        {
            klasseErrorMessage = "Cette classe ne peut pas être modifiée.";
            return;
        }

        klasseForm = new CourseFormModel
        {
            CourseId = klasse.CourseId,
            Code = klasse.Code,
            Title = klasse.Title,
            KlassenStufeId = klasse.KlassenStufeId,
            Description = klasse.Description,
            Price = klasse.Price,
            MaxPlaetze = klasse.MaxPlaetze,
            CurrentStudentCount = klasse.CurrentStudentCount,
            SchoolId = klasse.SchoolId,
            IstSystemNiveau = klasse.IstSystemNiveau,
            AutoAssignSkoulouAppAboOnCycleStart = klasse.AutoAssignSkoulouAppAboOnCycleStart,
            ConfirmAutoAssignActivationForActiveCycle = false
        };
        autoAssignConfirmOpen = false;
        klasseStatusMessage = null;
        klasseErrorMessage = null;
        klasseWarnMessage = null;
        klasseModalOpen = true;
    }

    private void OnKlasseModalOpenChanged(bool open)
    {
        klasseModalOpen = open;
        if (!open)
        {
            autoAssignConfirmOpen = false;
            klasseForm = new CourseFormModel();
        }
    }

    private void OnAutoAssignConfirmOpenChanged(bool open)
    {
        autoAssignConfirmOpen = open;
        if (!open)
        {
            klasseForm.ConfirmAutoAssignActivationForActiveCycle = false;
        }
    }

    private Task ConfirmAutoAssignActivationAsync() => RunDatabaseActionAsync(async () =>
    {
        autoAssignConfirmOpen = false;
        klasseForm.ConfirmAutoAssignActivationForActiveCycle = true;
        await SaveKlasseCoreAsync();
    });

    private Task SaveKlasseAsync() => RunDatabaseActionAsync(SaveKlasseCoreAsync);

    private async Task SaveKlasseCoreAsync()
    {
        klasseErrorMessage = null;
        klasseStatusMessage = null;
        klasseWarnMessage = null;

        if (klasseForm.SchoolId is not Guid schoolId)
        {
            klasseErrorMessage = "Sélectionnez l'école de cette classe.";
            return;
        }

        if (IsAutoAssignActivationPendingConfirmation && !klasseForm.ConfirmAutoAssignActivationForActiveCycle)
        {
            autoAssignConfirmOpen = true;
            return;
        }

        var result = await DashboardService.SaveSchoolKlasseForSchoolLeaderAsync(currentUserId, schoolId, klasseForm, ComponentToken);
        if (!result.Succeeded)
        {
            klasseForm.ConfirmAutoAssignActivationForActiveCycle = false;
            klasseErrorMessage = result.Message;
            return;
        }

        klasseStatusMessage = result.Message;
        var formReferenceName = ResolveFormReferenceName();
        if (!klasseForm.CourseId.HasValue
            && HasReferencePrefixMismatch(formReferenceName, klasseForm.Code, klasseForm.Title))
        {
            klasseWarnMessage = "Référence incohérente : vérifiez la correspondance entre nom de référence et classe (ex. CP1 ↔ CP1A/CP1B).";
        }
        klasseModalOpen = false;
        autoAssignConfirmOpen = false;
        klasseForm = new CourseFormModel();
        await LoadAsync();
    }

    private async Task OpenDurchlaufStartModalAsync(CourseFormModel klasse)
    {
        if (SelectedKlasseSchoolId is not Guid schoolId || !klasse.CourseId.HasValue)
        {
            return;
        }

        if (!await PruefeCycleZahlungsrechtAsync()) return;

        startKlasse = klasse;
        durchlaufStartDurchUeberweisungGesperrt = await FinanzService.HatNichtGemeldeteCycleUeberweisungAsync(schoolId, klasse.CourseId.Value, ComponentToken);
        durchlaufPreis = await FinanzService.GetPreisVorschlagAsync(schoolId, klasse.CourseId.Value, ComponentToken);
        durchlaufStartDatum = DateTime.Today;
        klasseErrorMessage = null;
        durchlaufStartModalOpen = true;
    }

    private Task StartDurchlaufAsync() => RunDatabaseActionAsync(StartDurchlaufCoreAsync);

    private async Task StartDurchlaufCoreAsync()
    {
        if (!await PruefeCycleZahlungsrechtAsync()) return;

        if (SelectedKlasseSchoolId is not Guid schoolId || startKlasse?.CourseId is not Guid klasseId)
        {
            return;
        }

        if (await FinanzService.HatNichtGemeldeteCycleUeberweisungAsync(schoolId, klasseId, ComponentToken))
        {
            durchlaufStartDurchUeberweisungGesperrt = true;
            klasseErrorMessage = "Déclarez d'abord le virement de la facturation du cycle précédent.";
            return;
        }

        var result = await FinanzService.DurchlaufStartenAsync(
            schoolId, klasseId, durchlaufPreis, durchlaufStartDatum, currentUserId,
            istSuperAdmin: false, autorisierungErzwingen: true, cancellationToken: ComponentToken);
        if (!result.Succeeded)
        {
            klasseErrorMessage = result.ErrorMessage;
            return;
        }

        durchlaufStartModalOpen = false;
        klasseStatusMessage = $"Le cycle de {startKlasse.Title} a démarré.";
        startKlasse = null;
        await ReloadSchoolLeaderStatusDataAsync();
    }

    private async Task ReloadSchoolLeaderStatusDataAsync()
    {
        if (currentUserId == Guid.Empty)
        {
            return;
        }

        try
        {
            students = await DashboardService.GetSchoolLeaderStudentsAsync(currentUserId, ComponentToken);
            archiveSelectedStudentIds.IntersectWith(students.Where(IsStudentArchivable).Select(student => student.UserId).ToHashSet());
            await LoadSchoolKlassenAsync();
            await LoadSchoolFachOptionsAsync();
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private async Task OpenDurchlaufEndeModal(DurchlaufUebersicht durchlauf)
    {
        if (!await PruefeCycleZahlungsrechtAsync()) return;

        zuBeendenderDurchlauf = durchlauf;
        (beendenTeilnehmerGesamt, beendenOhneNote) = await FinanzService.GetAbschlussNotenStatusAsync(durchlauf.DurchlaufID, ComponentToken);
        durchlaufEndDatum = DateTime.Today < durchlauf.StartDatum ? durchlauf.StartDatum : DateTime.Today;
        klasseErrorMessage = null;
        durchlaufEndeModalOpen = true;
    }

    private Task BeendeDurchlaufAsync()
    {
        durchlaufEndeEndgueltigModalOpen = true;
        return Task.CompletedTask;
    }

    private Task BeendeDurchlaufEndgueltigAsync()
        => RunDatabaseActionAsync(BeendeDurchlaufCoreAsync);

    private async Task BeendeDurchlaufCoreAsync()
    {
        if (!await PruefeCycleZahlungsrechtAsync()) return;

        if (zuBeendenderDurchlauf is null)
        {
            return;
        }

        var (succeeded, error, offeneSchuld, abrechnungsMeldung) = !isKoordinator
            ? await FinanzService.DurchlaufBeendenMitAutomatischerSkoulouAppAbrechnungAsync(
                zuBeendenderDurchlauf.DurchlaufID, durchlaufEndDatum, currentUserId,
                autorisierungErzwingen: true, cancellationToken: ComponentToken)
            : await BeendeOhneAutomatischeRechnungAsync(zuBeendenderDurchlauf.DurchlaufID);
        if (!succeeded)
        {
            klasseErrorMessage = error;
            return;
        }

        klasseStatusMessage = offeneSchuld > 0
            ? $"Le cycle est clôturé. La dette restante de {offeneSchuld.ToString("N0", Fr)} FCFA est indiquée comme perte."
            : "Le cycle est clôturé.";
        if (beendenOhneNote > 0)
            klasseStatusMessage += $" {beendenOhneNote} élève(s) ont été clôturés sans note finale ou décision complète.";
        if (abrechnungsMeldung is not null)
            klasseStatusMessage += $" {abrechnungsMeldung}";
        durchlaufEndeModalOpen = false;
        durchlaufEndeEndgueltigModalOpen = false;
        zuBeendenderDurchlauf = null;
        await LoadSchoolKlassenAsync();
    }

    private async Task<(bool Succeeded, string ErrorMessage, decimal OffeneSchuld, string? AutomatischeAbrechnungMessage)> BeendeOhneAutomatischeRechnungAsync(Guid durchlaufId)
    {
        var (succeeded, error, offeneSchuld) = await FinanzService.DurchlaufBeendenAsync(
            durchlaufId, durchlaufEndDatum, currentUserId,
            autorisierungErzwingen: true, cancellationToken: ComponentToken);
        return (succeeded, error, offeneSchuld, null);
    }

}
