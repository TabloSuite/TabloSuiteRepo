using HalloApp.Core.Models.Admin;
using HalloApp.Core.Models.Dashboard;
using HalloApp.Core.Models.Billing;
using HalloApp.Core.Services;
using HalloApp.WebApp.Components.Shared;
using HalloApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using System.Text;

namespace HalloApp.WebApp.Components.Pages;

public partial class Admin
{
    private bool CanSuggestHalloAppEmail => CanCurrentUserUseHalloAppEmail
        && !string.IsNullOrWhiteSpace(userForm.Vorname)
        && !string.IsNullOrWhiteSpace(userForm.Nachname);

    private string halloAppEmailButtonLabel => "E-mail auto";

    private void SuggestHalloAppEmail()
    {
        if (CanSuggestHalloAppEmail)
        {
            userForm.Email = BuildHalloAppEmail(userForm.Vorname, userForm.Nachname);
        }
    }

    private bool ShowHalloAppEmailButton => CanCurrentUserUseHalloAppEmail;
    private bool CanCurrentUserUseHalloAppEmail => canCurrentUserUseHalloAppEmail;

    private decimal SchoolSubscriptionPriceDisplay
        => schoolForm.SubscriptionPriceOverride ?? commissionSettings.GlobalSubscriptionPrice;

    private decimal EffectiveSchoolLeaderCommissionPercent
        => schoolForm.SchoolLeaderCommissionPercentOverride ?? commissionSettings.SchoolLeaderPercent;

    private decimal EffectiveTeacherCommissionPercent
        => schoolForm.TeacherCommissionPercentOverride ?? commissionSettings.TeacherPercent;

    private decimal EffectiveProductOwnerCommissionPercent
        => schoolForm.ProductOwnerCommissionPercentOverride ?? commissionSettings.ProductOwnerPercent;

    private decimal EffectiveSchoolLeaderCommissionAmount
        => Math.Round(SchoolSubscriptionPriceDisplay * EffectiveSchoolLeaderCommissionPercent / 100m, 2, MidpointRounding.AwayFromZero);

    private decimal EffectiveTeacherCommissionAmount
        => Math.Round(SchoolSubscriptionPriceDisplay * EffectiveTeacherCommissionPercent / 100m, 2, MidpointRounding.AwayFromZero);

    private decimal EffectiveProductOwnerCommissionAmount
        => Math.Round(SchoolSubscriptionPriceDisplay * EffectiveProductOwnerCommissionPercent / 100m, 2, MidpointRounding.AwayFromZero);

    private decimal SchoolLeaderCommissionFieldValue
    {
        get => schoolForm.SchoolLeaderCommissionPercentOverride ?? commissionSettings.SchoolLeaderPercent;
        set => schoolForm.SchoolLeaderCommissionPercentOverride = value;
    }

    private decimal TeacherCommissionFieldValue
    {
        get => schoolForm.TeacherCommissionPercentOverride ?? commissionSettings.TeacherPercent;
        set => schoolForm.TeacherCommissionPercentOverride = value;
    }

    private decimal ProductOwnerCommissionFieldValue
    {
        get => schoolForm.ProductOwnerCommissionPercentOverride ?? commissionSettings.ProductOwnerPercent;
        set => schoolForm.ProductOwnerCommissionPercentOverride = value;
    }

    private static string BuildHalloAppEmail(string vorname, string nachname)
    {
        var source = $"{vorname}.{nachname}".Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(source.Length);
        foreach (var character in source)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        var localPart = string.Join('.', builder.ToString().Split(new[] { '.', ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.All(char.IsLetterOrDigit)));
        return $"{localPart}@skoulou.com";
    }

    [Inject]
    private DashboardService DashboardService { get; set; } = default!;

    [Inject]
    private AboService AboService { get; set; } = default!;

    [Inject]
    private ProvisionsService CommissionService { get; set; } = default!;

    [Inject]
    private AuthService AuthService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    // Aufklappbares Abonnements-/Bezahl-Panel je Schüler.
    private Guid? expandedEnrollmentStudentId;
    private IReadOnlyList<LevelEnrollmentItem> levelEnrollments = [];

    // Bestätigungsdialog für die Zahlungsrücknahme (nur SuperAdmin).
    private bool confirmRemovePaymentOpen;
    private Guid pendingRemoveStudentId;
    private Guid pendingRemoveLevelId;
    private string pendingRemoveLevelCode = string.Empty;

    // Bestätigungsdialog für das Entfernen eines Abos (nur SuperAdmin).
    private bool confirmRemoveAboOpen;
    private Guid pendingRemoveAboStudentId;
    private Guid pendingRemoveAboLevelId;
    private string pendingRemoveAboLevelCode = string.Empty;

    // Ausschließlich Development: vollständiges Zurücksetzen einer Testschule
    // über den ihr zugeordneten Schulleiter.
    private bool confirmHardDeleteSchoolLeaderOpen;
    private Guid pendingHardDeleteSchoolLeaderId;
    private string pendingHardDeleteSchoolLeaderName = string.Empty;

    private DashboardSummary? summary;
    private IReadOnlyList<SchoolManagementViewModel> schoolManagementItems = [];
    private IReadOnlyList<TeacherAssignmentOption> teacherOptions = [];
    private IReadOnlyList<TeacherAssignmentOption> schoolOptions = [];
    private IReadOnlyList<CourseFormModel> userFormClassOptions = [];
    private IReadOnlyList<UserManagementItem> userManagementItems = [];
    private IReadOnlyList<CourseManagementViewModel> courseManagementItems = [];
    private IReadOnlyList<MediaLibraryItem> mediaLibraryItems = [];
    private IReadOnlyList<LessonManagementItem> lessonOptions = [];
    private SchoolFormModel schoolForm = new();
    private UserFormModel userForm = new();
    private CourseFormModel courseForm = new();

    private IReadOnlyList<ModernDropdownOption<string?>> SchoolCountryOptions
        => CountryOptions.WithCurrentValue(schoolForm.Country);

    private Task SetSchoolCountryAsync(string? country)
    {
        schoolForm.Country = country ?? string.Empty;
        return Task.CompletedTask;
    }
    private LessonFormModel lessonForm = new();
    private MediaUploadFormModel mediaUploadForm = new();
    private HashSet<Guid> selectedTeacherIds = [];
    private HashSet<Guid> selectedUserSchoolIds = [];
    private IBrowserFile? selectedMediaFile;
    private bool isDatabaseActionInProgress;
    private CommissionSettingsModel commissionSettings = new();
    private string? commissionStatusMessage;
    private string? commissionErrorMessage;

    [Inject]
    private DatabaseActionGuard DatabaseActionGuard { get; set; } = default!;

    [Inject]
    private IHostEnvironment HostEnvironment { get; set; } = default!;

    private bool IsDevelopment => HostEnvironment.IsDevelopment();
    private bool isProduktOwner;
    private bool canCurrentUserUseHalloAppEmail;

    // Filter der Nutzerliste auf eine bestimmte Schule (null = alle Schulen).
    private string? userSchoolFilter;
    private Guid? schoolDirectorFilter;
    private string courseScopeFilter = "all";
    private Guid? courseSchoolFilter;
    private Guid? mediaSchoolFilter;

    private IReadOnlyList<string> SchoolFilterNames =>
        schoolManagementItems.Select(s => s.Name).OrderBy(n => n).ToList();

    private IReadOnlyList<ModernDropdownOption<Guid?>> SchoolDirectorFilterOptions =>
    [
        new(null, "Tous les directeurs"),
        .. schoolManagementItems
            .SelectMany(school => school.DirectorAssignments)
            .GroupBy(director => director.TeacherId)
            .OrderBy(group => group.First().TeacherName)
            .Select(group => new ModernDropdownOption<Guid?>((Guid?)group.Key, group.First().TeacherName, group.First().Email))
    ];

    private IReadOnlyList<SchoolManagementViewModel> FilteredSchoolManagementItems =>
        schoolDirectorFilter.HasValue
            ? schoolManagementItems.Where(school => school.DirectorAssignments.Any(director => director.TeacherId == schoolDirectorFilter.Value)).ToList()
            : schoolManagementItems;

    private IReadOnlyList<UserManagementItem> FilteredUserManagementItems =>
        userSchoolFilter is null
            ? userManagementItems
            : userManagementItems.Where(u => u.Schools.Contains(userSchoolFilter)).ToList();

    private IReadOnlyList<CourseManagementViewModel> FilteredCourseManagementItems => courseManagementItems
        .Where(course => courseScopeFilter switch
        {
            "standard" => course.IstSystemNiveau || !course.SchoolId.HasValue,
            "school" => course.SchoolId.HasValue,
            _ => true
        })
        .Where(course => !courseSchoolFilter.HasValue || course.SchoolId == courseSchoolFilter.Value)
        .OrderBy(course => course.IstSystemNiveau ? 0 : 1)
        .ThenBy(course => course.SchoolName ?? string.Empty)
        .ThenBy(course => course.Code)
        .ToList();

    private IReadOnlyList<MediaLibraryItem> FilteredMediaLibraryItems => mediaLibraryItems
        .Where(media => !mediaSchoolFilter.HasValue || media.SchoolId == mediaSchoolFilter.Value)
        .OrderBy(media => media.SchoolName)
        .ThenBy(media => string.IsNullOrWhiteSpace(media.DisplayName) ? media.OriginalFileName : media.DisplayName)
        .ToList();

    private long TotalMediaBytes => mediaLibraryItems.Sum(media => media.Length);
    private long FilteredMediaBytes => FilteredMediaLibraryItems.Sum(media => media.Length);

    private IReadOnlyList<ModernDropdownOption<Guid?>> MediaSchoolFilterOptions =>
    [
        new(null, "Toutes les écoles"),
        .. mediaLibraryItems
            .Where(media => media.SchoolId.HasValue)
            .GroupBy(media => new { media.SchoolId, media.SchoolName })
            .OrderBy(group => group.Key.SchoolName)
            .Select(group => new ModernDropdownOption<Guid?>(group.Key.SchoolId, group.Key.SchoolName ?? "École sans nom", FormatFileSize(group.Sum(media => media.Length)), $"{group.Count()} média(s)"))
    ];

    private Task SetMediaSchoolFilterAsync(Guid? schoolId)
    {
        mediaSchoolFilter = schoolId;
        return Task.CompletedTask;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 Ko";
        }

        string[] units = ["o", "Ko", "Mo", "Go"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:N1} {units[unit]}";
    }

    private int StandardCourseCount => courseManagementItems.Count(course => course.IstSystemNiveau || !course.SchoolId.HasValue);
    private int SchoolSpecificCourseCount => courseManagementItems.Count(course => course.SchoolId.HasValue);

    private IReadOnlyList<ModernDropdownOption<Guid?>> CourseSchoolFilterOptions =>
    [
        new(null, "Toutes les écoles"),
        .. courseManagementItems
            .Where(course => course.SchoolId.HasValue)
            .GroupBy(course => new { course.SchoolId, course.SchoolName })
            .OrderBy(group => group.Key.SchoolName)
            .Select(group => new ModernDropdownOption<Guid?>(group.Key.SchoolId, group.Key.SchoolName ?? "École sans nom"))
    ];

    private void ShowAllCourses() => courseScopeFilter = "all";

    private void ShowStandardCourses() => courseScopeFilter = "standard";

    private void ShowSchoolCourses() => courseScopeFilter = "school";

    private string GetCourseScopeButtonClass(string scope, string activeClass) =>
        $"rounded-2xl px-4 py-3 text-left text-sm font-bold ring-1 transition {(courseScopeFilter == scope ? activeClass : "bg-white text-slate-700 ring-slate-200 hover:ring-emerald-200")}";

    private Task SetCourseSchoolFilterAsync(Guid? schoolId)
    {
        courseSchoolFilter = schoolId;
        return Task.CompletedTask;
    }

    private IReadOnlyList<ModernDropdownOption<string?>> RoleOptions =>
    [
        new("Schüler", "Élève"),
        new("Lehrer", "Enseignant"),
        new("SchulLeiter", "Directeur"),
        new("Koordinator", "Coordinateur"),
        new("ProduktOwner", "Responsable produit"),
        new("SuperAdmin", "Administrateur")
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> UserClassOptions =>
    [
        new(null, "À attribuer ultérieurement"),
        .. userFormClassOptions
            .OrderBy(klasse => klasse.Code)
            .ThenBy(klasse => klasse.Title)
            .Select(klasse => new ModernDropdownOption<Guid?>((Guid?)klasse.CourseId, $"{klasse.Code} · {klasse.Title}"))
    ];

    private IReadOnlyList<ModernDropdownOption<string?>> CefrOptions =>
    [
        new(string.Empty, "-- Choisir le niveau CECR --"),
        new("Pré-A1", "Pré-A1 — Découverte"),
        new("A1", "A1 — Niveau introductif"),
        new("A2", "A2 — Niveau intermédiaire"),
        new("B1", "B1 — Niveau seuil"),
        new("B2", "B2 — Niveau avancé"),
        new("C1", "C1 — Niveau autonome"),
        new("C2", "C2 — Maîtrise"),
        new("Hors CECR", "Hors CECR — classe spécifique")
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> CourseOptions =>
    [
        new((Guid?)Guid.Empty, "Veuillez choisir"),
        .. courseManagementItems.Select(course => new ModernDropdownOption<Guid?>((Guid?)course.CourseId, $"{course.Code} · {course.Title}"))
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> LessonOptions =>
    [
        new(null, "Veuillez choisir"),
        .. lessonOptions.Select(lesson => new ModernDropdownOption<Guid?>((Guid?)lesson.LessonId, lesson.Title))
    ];

    private IReadOnlyList<ModernDropdownOption<string?>> MediaTypeOptions =>
    [
        new("image", "Image"),
        new("audio", "Audio")
    ];

    private IReadOnlyList<ModernDropdownOption<string?>> LessonExerciseTypeOptions =>
    [
        new("SingleChoice", "Single Choice"),
        new("MultipleChoice", "Multiple Choice"),
        new("Text", "Text"),
        new("SentenceBuilding", "Sentence Building"),
        new("GapFill", "Gap Fill"),
        new("AudioText", "Audio Text"),
        new("AudioSingleChoice", "Audio Single Choice"),
        new("AudioMultipleChoice", "Audio Multiple Choice")
    ];

    private IReadOnlyList<ModernDropdownOption<string?>> TtsLanguageOptions =>
    [
        new("de-DE", "Deutsch (DE)"),
        new("fr-FR", "Français (FR)"),
        new("en-US", "English (EN)")
    ];

    private string MediaFileAccept => string.Equals(mediaUploadForm.MediaType, "audio", StringComparison.OrdinalIgnoreCase)
        ? "audio/mpeg,audio/mp4,.mp3,.m4a"
        : "image/png,image/jpeg,image/gif,image/webp";

    private void SetUserSchoolFilter(string? schoolName) => userSchoolFilter = schoolName;

    private Task SetSchoolDirectorFilterAsync(Guid? directorId)
    {
        schoolDirectorFilter = directorId;
        return Task.CompletedTask;
    }

    private async Task SetUserRoleAsync(string? roleName)
    {
        userForm.RoleName = roleName ?? "Schüler";
        await OnUserRoleChangedAsync();
    }

    private Task SetUserClassAsync(Guid? classId)
    {
        userForm.CurrentKlasseId = classId;
        return Task.CompletedTask;
    }

    private Task SetCourseCefrAsync(string? cefrGroup)
    {
        courseForm.CEFRGroup = cefrGroup ?? string.Empty;
        return Task.CompletedTask;
    }

    private Task SetLessonCourseAsync(Guid? courseId)
    {
        lessonForm.CourseId = courseId ?? Guid.Empty;
        return Task.CompletedTask;
    }

    private static Task SetLessonExerciseTypeAsync(LessonExerciseFormModel exercise, string? exerciseType)
    {
        exercise.ExerciseType = NormalizeLessonExerciseType(exerciseType);

        if (IsChoiceType(exercise.ExerciseType))
        {
            if (exercise.Options.Count == 0)
            {
                exercise.Options =
                [
                    new LessonExerciseOptionFormModel { IsCorrect = true },
                    new LessonExerciseOptionFormModel()
                ];
            }
        }
        else
        {
            exercise.Options.Clear();
        }

        if (exercise.ExerciseType == "GapFill")
        {
            if (exercise.Gaps.Count == 0)
            {
                exercise.Gaps =
                [
                    new LessonExerciseGapFormModel()
                ];
            }

            exercise.AcceptedAnswers = null;
        }
        else
        {
            exercise.Gaps.Clear();
        }

        if (!IsAudioType(exercise.ExerciseType))
        {
            exercise.AudioText = null;
            exercise.TtsLanguage = "de-DE";
        }

        return Task.CompletedTask;
    }

    private static Task SetLessonExerciseTtsLanguageAsync(LessonExerciseFormModel exercise, string? language)
    {
        exercise.TtsLanguage = language switch
        {
            "fr-FR" => "fr-FR",
            "en-US" => "en-US",
            _ => "de-DE"
        };

        return Task.CompletedTask;
    }

    private void AddLessonExercise()
    {
        lessonForm.Exercises.Add(CreateDefaultLessonExercise());
        ReorderLessonExercises();
    }

    private void RemoveLessonExercise(LessonExerciseFormModel exercise)
    {
        lessonForm.Exercises.Remove(exercise);
        ReorderLessonExercises();
    }

    private static LessonExerciseFormModel CreateDefaultLessonExercise()
        => new()
        {
            Points = 10,
            ExerciseType = "SingleChoice",
            TtsLanguage = "de-DE",
            Options =
            [
                new LessonExerciseOptionFormModel { IsCorrect = true },
                new LessonExerciseOptionFormModel()
            ]
        };

    private static void AddLessonExerciseOption(LessonExerciseFormModel exercise)
        => exercise.Options.Add(new LessonExerciseOptionFormModel());

    private static void RemoveLessonExerciseOption(LessonExerciseFormModel exercise, LessonExerciseOptionFormModel option)
        => exercise.Options.Remove(option);

    private static void AddLessonGap(LessonExerciseFormModel exercise)
        => exercise.Gaps.Add(new LessonExerciseGapFormModel());

    private static void RemoveLessonGap(LessonExerciseFormModel exercise, LessonExerciseGapFormModel gap)
        => exercise.Gaps.Remove(gap);

    private static void SelectSingleCorrectLessonOption(LessonExerciseFormModel exercise, LessonExerciseOptionFormModel selected)
    {
        foreach (var option in exercise.Options)
        {
            option.IsCorrect = ReferenceEquals(option, selected);
        }
    }

    private void ReorderLessonExercises()
    {
        for (var i = 0; i < lessonForm.Exercises.Count; i++)
        {
            lessonForm.Exercises[i].Order = i + 1;
        }
    }

    private static bool IsChoiceType(string? exerciseType)
    {
        var normalized = NormalizeLessonExerciseType(exerciseType);
        return normalized is "SingleChoice" or "MultipleChoice" or "AudioSingleChoice" or "AudioMultipleChoice";
    }

    private static bool IsSingleChoiceType(string? exerciseType)
    {
        var normalized = NormalizeLessonExerciseType(exerciseType);
        return normalized is "SingleChoice" or "AudioSingleChoice";
    }

    private static bool IsAudioType(string? exerciseType)
    {
        var normalized = NormalizeLessonExerciseType(exerciseType);
        return normalized is "AudioText" or "AudioSingleChoice" or "AudioMultipleChoice";
    }

    private static string NormalizeLessonExerciseType(string? exerciseType)
    {
        if (string.IsNullOrWhiteSpace(exerciseType))
        {
            return "SingleChoice";
        }

        return exerciseType.Trim().ToLowerInvariant() switch
        {
            "choice" or "singlechoice" => "SingleChoice",
            "multiplechoice" => "MultipleChoice",
            "audiochoice" or "audiosinglechoice" => "AudioSingleChoice",
            "audiomultiplechoice" => "AudioMultipleChoice",
            "gapfill" or "gap" or "lücken" or "luecken" => "GapFill",
            "text" or "freetext" => "Text",
            "sentencebuilding" or "sentence" or "satzbau" => "SentenceBuilding",
            "audiotext" or "audio" => "AudioText",
            _ => "SingleChoice"
        };
    }

    private Task SetMediaLessonAsync(Guid? lessonId)
    {
        mediaUploadForm.LessonId = lessonId;
        return Task.CompletedTask;
    }

    private Task SetMediaTypeAsync(string? mediaType)
    {
        mediaUploadForm.MediaType = mediaType ?? "image";
        return Task.CompletedTask;
    }
    private string? statusMessage;
    private string? errorMessage;
    private string? userStatusMessage;
    private string? userErrorMessage;
    private string? courseStatusMessage;
    private string? courseErrorMessage;
    private string? mediaStatusMessage;
    private string? mediaErrorMessage;

    private bool schoolOverviewExpanded;
    private bool usersExpanded;
    private bool mediaExpanded;
    private bool coursesExpanded;

    private bool userModalOpen;

    private string userModalTitle =>
        userForm.UserId.HasValue ? "Modifier l'utilisateur" : "Nouvel utilisateur";

    private bool schoolModalOpen;

    private string schoolModalTitle =>
        schoolForm.Id.HasValue ? "Modifier l'école" : "Nouvelle école";

    private bool courseModalOpen;

    private string courseModalTitle =>
        courseForm.CourseId.HasValue ? "Modifier la classe / le niveau" : "Nouvelle classe / nouveau niveau";

    private bool lessonModalOpen;

    private string lessonModalTitle =>
        lessonForm.LessonId.HasValue ? "Modifier la leçon" : "Nouvelle leçon";

    private bool mediaModalOpen;

    private bool AllSectionsExpanded =>
        schoolOverviewExpanded && usersExpanded && mediaExpanded && coursesExpanded;

    private void ToggleAllSections()
    {
        var next = !AllSectionsExpanded;
        schoolOverviewExpanded = next;
        usersExpanded = next;
        mediaExpanded = next;
        coursesExpanded = next;
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            canCurrentUserUseHalloAppEmail = !authState.User.IsInRole(Rollen.Lehrer)
                && !authState.User.IsInRole(Rollen.Schueler);
            isProduktOwner = authState.User.IsInRole("ProduktOwner") && !authState.User.IsInRole("SuperAdmin");
            await LoadAsync();
            commissionSettings = await CommissionService.GetCommissionSettingsAsync();
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private async Task LoadAsync()
    {
        summary = await DashboardService.GetAdminSummaryAsync();
        schoolManagementItems = await DashboardService.GetSchoolManagementAsync();
        if (schoolDirectorFilter.HasValue && !schoolManagementItems.Any(school => school.DirectorAssignments.Any(director => director.TeacherId == schoolDirectorFilter.Value)))
        {
            schoolDirectorFilter = null;
        }
        teacherOptions = await DashboardService.GetTeacherOptionsAsync(schoolForm.Id);
        schoolOptions = await DashboardService.GetSchoolOptionsAsync(userForm.UserId);
        userManagementItems = (await DashboardService.GetUserManagementAsync())
            .OrderBy(u => GetRoleRank(u.RoleName))
            .ThenBy(u => u.FullName)
            .ToList();
        courseManagementItems = await DashboardService.GetCourseManagementAsync();
        mediaLibraryItems = await DashboardService.GetMediaLibraryAsync();
        lessonOptions = await DashboardService.GetLessonOptionsAsync();
    }

    private Task SaveSchoolAsync() => RunDatabaseActionAsync(SaveSchoolCoreAsync);

    private async Task SaveSchoolCoreAsync()
    {
        errorMessage = null;
        statusMessage = null;

        var result = await DashboardService.SaveSchoolAsync(schoolForm, selectedTeacherIds.ToList());
        if (!result.Succeeded)
        {
            errorMessage = result.Message;
            return;
        }

        statusMessage = result.Message;
        await ResetFormAsync();
        schoolModalOpen = false;
        await LoadAsync();
    }

    private void RequestHardDeleteSchoolLeader(UserManagementItem user)
    {
        if (!IsDevelopment || user.RoleName != "SchulLeiter")
        {
            return;
        }

        pendingHardDeleteSchoolLeaderId = user.UserId;
        pendingHardDeleteSchoolLeaderName = user.FullName;
        confirmHardDeleteSchoolLeaderOpen = true;
    }

    private Task ConfirmHardDeleteSchoolLeaderAsync() => RunDatabaseActionAsync(ConfirmHardDeleteSchoolLeaderCoreAsync);

    private async Task ConfirmHardDeleteSchoolLeaderCoreAsync()
    {
        confirmHardDeleteSchoolLeaderOpen = false;
        userErrorMessage = null;
        userStatusMessage = null;

        var result = await DashboardService.HardDeleteSchoolLeaderForDevelopmentAsync(pendingHardDeleteSchoolLeaderId);
        if (!result.Succeeded)
        {
            userErrorMessage = result.Message;
            return;
        }

        userStatusMessage = result.Message;
        await LoadAsync();
    }

    private async Task EditSchoolAsync(Guid schoolId)
    {
        var model = await DashboardService.GetSchoolForEditAsync(schoolId);
        if (model is null)
        {
            errorMessage = "Impossible de charger l'école.";
            return;
        }

        schoolForm = model;
        teacherOptions = await DashboardService.GetTeacherOptionsAsync(schoolId);
        selectedTeacherIds = teacherOptions.Where(x => x.IsAssigned).Select(x => x.TeacherId).ToHashSet();
        statusMessage = null;
        errorMessage = null;
        schoolModalOpen = true;
    }

    private async Task OpenNewSchoolModalAsync()
    {
        await ResetFormAsync();
        statusMessage = null;
        errorMessage = null;
        schoolModalOpen = true;
    }

    private async Task CloseSchoolModalAsync()
    {
        schoolModalOpen = false;
        await ResetFormAsync();
    }

    private async Task OnSchoolModalOpenChangedAsync(bool open)
    {
        schoolModalOpen = open;
        if (!open)
        {
            await ResetFormAsync();
        }
    }

    private async Task SaveCommissionSettingsAsync()
    {
        commissionStatusMessage = null;
        commissionErrorMessage = null;
        await RunDatabaseActionAsync(async () =>
        {
            try
            {
                await CommissionService.SaveCommissionSettingsAsync(commissionSettings);
                commissionStatusMessage = "Les commissions seront appliquées aux nouveaux abonnements.";
            }
            catch (InvalidOperationException exception)
            {
                commissionErrorMessage = exception.Message;
            }
        });
    }

    private Task SetSchoolLeaderCommissionAmountAsync(decimal amount)
    {
        if (commissionSettings.GlobalSubscriptionPrice > 0m)
        {
            commissionSettings.SchoolLeaderPercent = Math.Round(amount / commissionSettings.GlobalSubscriptionPrice * 100m, 2, MidpointRounding.AwayFromZero);
        }

        return Task.CompletedTask;
    }

    private Task SetTeacherCommissionAmountAsync(decimal amount)
    {
        if (commissionSettings.GlobalSubscriptionPrice > 0m)
        {
            commissionSettings.TeacherPercent = Math.Round(amount / commissionSettings.GlobalSubscriptionPrice * 100m, 2, MidpointRounding.AwayFromZero);
        }

        return Task.CompletedTask;
    }

    private Task SetProductOwnerCommissionAmountAsync(decimal amount)
    {
        if (commissionSettings.GlobalSubscriptionPrice > 0m)
        {
            commissionSettings.ProductOwnerPercent = Math.Round(amount / commissionSettings.GlobalSubscriptionPrice * 100m, 2, MidpointRounding.AwayFromZero);
        }

        return Task.CompletedTask;
    }

    private async Task ResetFormAsync()
    {
        schoolForm = new SchoolFormModel();
        selectedTeacherIds = [];
        teacherOptions = await DashboardService.GetTeacherOptionsAsync();
    }

    private Task SaveUserAsync() => RunDatabaseActionAsync(SaveUserCoreAsync);

    private async Task SaveUserCoreAsync()
    {
        userErrorMessage = null;
        userStatusMessage = null;
        userForm.SchoolIds = userForm.RoleName is "ProduktOwner" or "SuperAdmin"
            ? []
            : selectedUserSchoolIds.ToList();

        if (isProduktOwner && userForm.RoleName is "ProduktOwner" or "SuperAdmin")
        {
            userErrorMessage = "Seul l'Administrateur peut créer ou modifier des Product Owners et des Administrateurs.";
            return;
        }

        // Vor dem Speichern merken, ob es sich um eine Neuanlage handelt:
        // Nur dann wird anschließend eine Willkommensmail versendet.
        var istNeuanlage = !userForm.UserId.HasValue;
        var email = userForm.Email?.Trim() ?? string.Empty;
        var vorname = userForm.Vorname?.Trim() ?? string.Empty;

        var result = await DashboardService.SaveUserAsync(userForm);
        if (!result.Succeeded)
        {
            userErrorMessage = result.Message;
            return;
        }

        if (istNeuanlage)
        {
            // Best-Effort: Ein fehlgeschlagener Versand (z. B. nicht existierende
            // Adresse) darf die Benutzeranlage nicht beeinträchtigen.
            await AuthService.SendWelcomeEmailAsync(email, vorname);
        }

        userStatusMessage = result.Message;
        await ResetUserFormAsync();
        userModalOpen = false;
        await LoadAsync();
    }

    private async Task EditUserAsync(Guid userId)
    {
        var model = await DashboardService.GetUserForEditAsync(userId);
        if (model is null)
        {
            userErrorMessage = "Impossible de charger l'utilisateur.";
            return;
        }

        userForm = model;
        if (isProduktOwner && userForm.RoleName is "ProduktOwner" or "SuperAdmin")
        {
            userErrorMessage = "Seul l'Administrateur peut modifier des Product Owners et des Administrateurs.";
            userForm = new UserFormModel();
            return;
        }
        selectedUserSchoolIds = userForm.SchoolIds.ToHashSet();
        schoolOptions = await DashboardService.GetSchoolOptionsAsync(userId);
        await LoadUserFormClassOptionsAsync();
        userStatusMessage = null;
        userErrorMessage = null;
        userModalOpen = true;
    }

    private Task SetUserBlockedAsync(Guid userId, bool isBlocked) => RunDatabaseActionAsync(() => SetUserBlockedCoreAsync(userId, isBlocked));

    private async Task SetUserBlockedCoreAsync(Guid userId, bool isBlocked)
    {
        if (isProduktOwner)
        {
            userErrorMessage = "La gestion des blocages de comptes est réservée à l'Administrateur.";
            return;
        }

        userErrorMessage = null;
        userStatusMessage = null;

        await DashboardService.SetStudentBlockedStateAsync(userId, isBlocked, allowBlockingPaidStudent: true);
        userStatusMessage = isBlocked ? "L'utilisateur a été bloqué." : "L'utilisateur a été activé.";
        await LoadAsync();
    }

    private async Task ToggleEnrollmentPanelAsync(Guid studentId)
    {
        if (expandedEnrollmentStudentId == studentId)
        {
            expandedEnrollmentStudentId = null;
            levelEnrollments = [];
            return;
        }

        expandedEnrollmentStudentId = studentId;
        await LoadAsync();
        levelEnrollments = await DashboardService.GetStudentLevelEnrollmentsAsync(studentId);
    }

    private void RequestRemovePayment(Guid studentId, LevelEnrollmentItem level)
    {
        pendingRemoveStudentId = studentId;
        pendingRemoveLevelId = level.KlasseId;
        pendingRemoveLevelCode = level.CourseCode;
        confirmRemovePaymentOpen = true;
    }

    private Task ConfirmRemovePaymentAsync() => RunDatabaseActionAsync(ConfirmRemovePaymentCoreAsync);

    private async Task ConfirmRemovePaymentCoreAsync()
    {
        if (isProduktOwner)
        {
            confirmRemovePaymentOpen = false;
            userErrorMessage = "La gestion des paiements est réservée à l'Administrateur.";
            return;
        }

        confirmRemovePaymentOpen = false;
        var (succeeded, resultMessage) = await AboService.BezahltZuruecknehmenAsync(
            pendingRemoveStudentId,
            pendingRemoveLevelId,
            istSuperAdmin: true);
        if (succeeded)
        {
            userStatusMessage = resultMessage;
            userErrorMessage = null;
        }
        else
        {
            userErrorMessage = resultMessage;
            userStatusMessage = null;
        }

        await LoadAsync();
        expandedEnrollmentStudentId = pendingRemoveStudentId;
        levelEnrollments = await DashboardService.GetStudentLevelEnrollmentsAsync(pendingRemoveStudentId);
    }

    private void RequestRemoveAbo(Guid studentId, LevelEnrollmentItem level)
    {
        pendingRemoveAboStudentId = studentId;
        pendingRemoveAboLevelId = level.KlasseId;
        pendingRemoveAboLevelCode = level.CourseCode;
        confirmRemoveAboOpen = true;
    }

    private Task ConfirmRemoveAboAsync() => RunDatabaseActionAsync(ConfirmRemoveAboCoreAsync);

    private async Task ConfirmRemoveAboCoreAsync()
    {
        if (isProduktOwner)
        {
            confirmRemoveAboOpen = false;
            userErrorMessage = "La gestion des abonnements est réservée à l'Administrateur.";
            return;
        }

        confirmRemoveAboOpen = false;
        var (succeeded, resultMessage) = await AboService.AboEntfernenAsync(pendingRemoveAboStudentId, pendingRemoveAboLevelId, istSuperAdmin: true);
        if (succeeded)
        {
            userStatusMessage = resultMessage;
            userErrorMessage = null;
        }
        else
        {
            userErrorMessage = resultMessage;
            userStatusMessage = null;
        }

        await LoadAsync();
        expandedEnrollmentStudentId = pendingRemoveAboStudentId;
        levelEnrollments = await DashboardService.GetStudentLevelEnrollmentsAsync(pendingRemoveAboStudentId);
    }

    private async Task OpenNewUserModalAsync()
    {
        await ResetUserFormAsync();
        userStatusMessage = null;
        userErrorMessage = null;
        userModalOpen = true;
    }

    private async Task CloseUserModalAsync()
    {
        userModalOpen = false;
        await ResetUserFormAsync();
    }

    private async Task OnUserModalOpenChangedAsync(bool open)
    {
        userModalOpen = open;
        if (!open)
        {
            await ResetUserFormAsync();
        }
    }

    private async Task ResetUserFormAsync()
    {
        userForm = new UserFormModel();
        selectedUserSchoolIds = [];
        schoolOptions = await DashboardService.GetSchoolOptionsAsync();
        userFormClassOptions = [];
    }

    private async Task ToggleUserSchoolAsync(Guid schoolId, object? value)
    {
        var isChecked = value as bool? == true || string.Equals(value?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        if (userForm.RoleName == "Schüler")
        {
            selectedUserSchoolIds = isChecked ? [schoolId] : [];
            userForm.CurrentKlasseId = null;
        }
        else if (isChecked)
        {
            selectedUserSchoolIds.Add(schoolId);
        }
        else
        {
            selectedUserSchoolIds.Remove(schoolId);
            userForm.TeacherClassAssignments.RemoveAll(assignment => assignment.SchoolId == schoolId);
        }

        await LoadUserFormClassOptionsAsync();
    }

    private async Task OnUserRoleChangedAsync()
    {
        if (userForm.RoleName is "ProduktOwner" or "SuperAdmin")
        {
            selectedUserSchoolIds = [];
        }

        if (userForm.RoleName != "Schüler")
        {
            userForm.CurrentKlasseId = null;
        }

        if (userForm.RoleName != "Lehrer")
        {
            userForm.TeacherClassAssignments = [];
        }

        await LoadUserFormClassOptionsAsync();
    }

    private async Task LoadUserFormClassOptionsAsync()
    {
        var classes = new List<CourseFormModel>();
        foreach (var schoolId in selectedUserSchoolIds)
        {
            classes.AddRange(await DashboardService.GetSchoolKlassenAsync(schoolId, ComponentToken));
        }

        userFormClassOptions = classes;
    }

    private bool IsTeacherClassAssigned(Guid schoolId, Guid klasseId) => userForm.TeacherClassAssignments
        .Any(assignment => assignment.SchoolId == schoolId && assignment.KlasseId == klasseId);

    private void SetTeacherClassAssignment(CourseFormModel klasse, bool isAssigned)
    {
        if (klasse.SchoolId is not Guid schoolId || klasse.CourseId is not Guid klasseId)
        {
            return;
        }

        userForm.TeacherClassAssignments.RemoveAll(assignment => assignment.SchoolId == schoolId && assignment.KlasseId == klasseId);
        if (isAssigned)
        {
            userForm.TeacherClassAssignments.Add(new TeacherClassAssignmentFormModel
            {
                SchoolId = schoolId,
                KlasseId = klasseId
            });
        }
    }

    private Task SaveCourseAsync() => RunDatabaseActionAsync(SaveCourseCoreAsync);

    private async Task SaveCourseCoreAsync()
    {
        courseErrorMessage = null;
        courseStatusMessage = null;

        var result = await DashboardService.SaveCourseAsync(courseForm);
        if (!result.Succeeded)
        {
            courseErrorMessage = result.Message;
            return;
        }

        courseStatusMessage = result.Message;
        if (result.CourseId.HasValue)
        {
            lessonForm.CourseId = result.CourseId.Value;
        }

        ResetCourseForm();
        courseModalOpen = false;
        await LoadAsync();
    }

    private async Task EditCourseAsync(Guid courseId)
    {
        var model = await DashboardService.GetCourseForEditAsync(courseId);
        if (model is null)
        {
            courseErrorMessage = "Impossible de charger le cours.";
            return;
        }

        courseForm = model;
        courseStatusMessage = null;
        courseErrorMessage = null;
        courseModalOpen = true;
    }

    private void ResetCourseForm()
    {
        courseForm = new CourseFormModel();
    }

    private void OpenNewCourseModal()
    {
        ResetCourseForm();
        courseStatusMessage = null;
        courseErrorMessage = null;
        courseModalOpen = true;
    }

    private void CloseCourseModal()
    {
        courseModalOpen = false;
        ResetCourseForm();
    }

    private void OnCourseModalOpenChanged(bool open)
    {
        courseModalOpen = open;
        if (!open)
        {
            ResetCourseForm();
        }
    }

    private Task SaveLessonAsync() => RunDatabaseActionAsync(SaveLessonCoreAsync);

    private async Task SaveLessonCoreAsync()
    {
        courseErrorMessage = "La création et la modification des leçons sont réservées aux enseignants.";
        courseStatusMessage = null;
        return;

        courseErrorMessage = null;
        courseStatusMessage = null;

        ReorderLessonExercises();

        var result = await DashboardService.SaveLessonAsync(lessonForm);
        if (!result.Succeeded)
        {
            courseErrorMessage = result.Message;
            return;
        }

        courseStatusMessage = result.Message;
        ResetLessonForm();
        lessonModalOpen = false;
        await LoadAsync();
    }

    private async Task EditLessonAsync(Guid lessonId)
    {
        courseErrorMessage = "La gestion des leçons se fait dans l'espace enseignant.";
        courseStatusMessage = null;
        return;

        var model = await DashboardService.GetLessonForEditAsync(lessonId);
        if (model is null)
        {
            courseErrorMessage = "Impossible de charger la leçon.";
            return;
        }

        lessonForm = model;
        if (lessonForm.Exercises.Count == 0)
        {
            lessonForm.Exercises.Add(CreateDefaultLessonExercise());
            ReorderLessonExercises();
        }
        courseStatusMessage = null;
        courseErrorMessage = null;
        lessonModalOpen = true;
    }

    private void ResetLessonForm()
    {
        lessonForm = new LessonFormModel
        {
            Exercises =
            [
                CreateDefaultLessonExercise()
            ]
        };
        ReorderLessonExercises();
    }

    private void OpenNewLessonModal()
    {
        courseErrorMessage = "La création de leçons est réservée aux enseignants.";
        courseStatusMessage = null;
    }

    private void CloseLessonModal()
    {
        lessonModalOpen = false;
        ResetLessonForm();
    }

    private void OnLessonModalOpenChanged(bool open)
    {
        lessonModalOpen = open;
        if (!open)
        {
            ResetLessonForm();
        }
    }

    private void HandleMediaSelected(InputFileChangeEventArgs args)
    {
        selectedMediaFile = args.File;
    }

    private Task SaveMediaAsync() => RunDatabaseActionAsync(SaveMediaCoreAsync);

    private async Task SaveMediaCoreAsync()
    {
        mediaErrorMessage = null;
        mediaStatusMessage = null;

        if (selectedMediaFile is null)
        {
            mediaErrorMessage = "Veuillez sélectionner un fichier.";
            return;
        }

        await using var stream = selectedMediaFile.OpenReadStream(10 * 1024 * 1024);
        var result = await DashboardService.SaveLessonMediaAsync(mediaUploadForm, stream, selectedMediaFile.Name);
        if (!result.Succeeded)
        {
            mediaErrorMessage = result.Message;
            return;
        }

        mediaStatusMessage = result.Message;
        ResetMediaForm();
        mediaModalOpen = false;
        await LoadAsync();
    }

    private void ResetMediaForm()
    {
        mediaUploadForm = new MediaUploadFormModel();
        selectedMediaFile = null;
    }

    private void OpenNewMediaModal()
    {
        ResetMediaForm();
        mediaStatusMessage = null;
        mediaErrorMessage = null;
        mediaModalOpen = true;
    }

    private void CloseMediaModal()
    {
        mediaModalOpen = false;
        ResetMediaForm();
    }

    private void OnMediaModalOpenChanged(bool open)
    {
        mediaModalOpen = open;
        if (!open)
        {
            ResetMediaForm();
        }
    }

    private void ToggleTeacher(Guid teacherId, object? value)
    {
        var isChecked = value as bool? == true || string.Equals(value?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        if (isChecked)
        {
            selectedTeacherIds.Add(teacherId);
        }
        else
        {
            selectedTeacherIds.Remove(teacherId);
        }
    }

    private IReadOnlyList<DataListChip<UserManagementItem>> userRoleChips =>
    [
        new()
        {
            Key = "SchulLeiter",
            Label = "Directeurs",
            Accent = "indigo",
            Predicate = u => u.RoleName == "SchulLeiter",
            Count = FilteredUserManagementItems.Count(u => u.RoleName == "SchulLeiter")
        },
        new()
        {
            Key = "ProduktOwner",
            Label = "Responsables produit",
            Accent = "violet",
            Predicate = u => u.RoleName == "ProduktOwner",
            Count = FilteredUserManagementItems.Count(u => u.RoleName == "ProduktOwner")
        },
        new()
        {
            Key = "Koordinator",
            Label = "Coordinateurs",
            Accent = "cyan",
            Predicate = u => u.RoleName == "Koordinator",
            Count = FilteredUserManagementItems.Count(u => u.RoleName == "Koordinator")
        },
        new()
        {
            Key = "Lehrer",
            Label = "Enseignants",
            Accent = "blue",
            Predicate = u => u.RoleName == "Lehrer",
            Count = FilteredUserManagementItems.Count(u => u.RoleName == "Lehrer")
        },
        new()
        {
            Key = "Sch\u00fcler",
            Label = "\u00c9l\u00e8ves",
            Accent = "emerald",
            Predicate = u => u.RoleName == "Sch\u00fcler",
            Count = FilteredUserManagementItems.Count(u => u.RoleName == "Sch\u00fcler")
        }
    ];

    private static string GetRoleLabel(string roleName) => roleName switch
    {
        "SuperAdmin" => "Administrateur",
        "SchulLeiter" => "Directeur",
        "Koordinator" => "Coordinateur",
        "ProduktOwner" => "Responsable produit",
        "Lehrer" => "Enseignant",
        "Sch\u00fcler" => "\u00c9l\u00e8ve",
        _ => roleName
    };

    private static string GetStudentAboHeaderLabel(bool hasPaidEnrollment, bool isHalloPlus)
        => hasPaidEnrollment
            ? "Abo actuel : SKOULOUAPP pay\u00e9"
            : isHalloPlus
                ? "Abo actuel : SKOULOUAPP actif"
                : "Abo actuel : non pay\u00e9";

    private static string GetSchoolFeeHeaderLabel(decimal paidAmount, decimal? totalAmount, bool hasActiveCycle)
        => !hasActiveCycle || !totalAmount.HasValue
            ? "Scolarit\u00e9 : cycle finance \u00e0 d\u00e9marrer"
            : totalAmount.Value <= 0m
                ? "Scolarit\u00e9 : \u00e0 d\u00e9finir"
                : $"Scolarit\u00e9 : {paidAmount:N0} / {totalAmount.Value:N0} FCFA pay\u00e9";

    private static int GetRoleRank(string roleName) => roleName switch
    {
        "SuperAdmin" => 0,
        "ProduktOwner" => 1,
        "SchulLeiter" => 2,
        "Koordinator" => 3,
        "Lehrer" => 4,
        "Sch\u00fcler" => 5,
        _ => 6
    };

    private static string GetRoleSectionTitle(string roleName) => roleName switch
    {
        "SuperAdmin" => "Administrateurs",
        "ProduktOwner" => "Responsables produit",
        "SchulLeiter" => "Directeurs",
        "Koordinator" => "Coordinateurs",
        "Lehrer" => "Enseignants",
        "Schüler" => "Élèves",
        _ => "Autres"
    };

    private readonly HashSet<Guid> expandedCourseIds = [];

    private bool IsCourseExpanded(Guid courseId) => expandedCourseIds.Contains(courseId);

    private void ToggleCourse(Guid courseId)
    {
        if (!expandedCourseIds.Remove(courseId))
        {
            expandedCourseIds.Add(courseId);
        }
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
