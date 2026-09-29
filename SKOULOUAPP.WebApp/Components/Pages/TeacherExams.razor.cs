using System.Security.Claims;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.Models.Admin;
using HalloApp.Core.Interfaces;
using HalloApp.Core.Services;
using HalloApp.WebApp.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace HalloApp.WebApp.Components.Pages;

public partial class TeacherExams
{
    [Parameter]
    public bool Embedded { get; set; }

    [Inject]
    private ExamService ExamService { get; set; } = default!;

    [Inject]
    private IHalloAppDbContext DbContext { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    private ElementReference editorScrollContainer;

    private List<ExamManagementItem> exams = [];
    private IReadOnlyList<(Guid KlasseId, string Code, string Title, Guid? SchoolId, string SchoolName)> klasseOptions = [];
    private IReadOnlyList<(Guid SchoolId, string SchoolName)> schoolOptions = [];
    private Guid? selectedSchoolFilterId;
    private Guid? selectedKlasseFilterId;
    private ExamFormModel? form;
    private bool editorOpen;
    private bool isSavingExam;
    private string? formError;
    private string? statusMessage;
    private Guid currentTeacherId;
    private bool isSchoolDirectionUser;
    private IReadOnlyList<ModernDropdownOption<Guid?>> SchoolFilterOptions =>
    [
        new(null, "Toutes les écoles"),
        .. schoolOptions.Select(school => new ModernDropdownOption<Guid?>((Guid?)school.SchoolId, school.SchoolName))
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> KlasseFilterOptions
    {
        get
        {
            var filtered = selectedSchoolFilterId.HasValue
                ? klasseOptions.Where(k => k.SchoolId == selectedSchoolFilterId.Value)
                : klasseOptions;

            return
            [
                new(null, "Toutes les classes"),
                .. filtered
                    .OrderBy(k => k.Code)
                    .Select(k => new ModernDropdownOption<Guid?>((Guid?)k.KlasseId, FormatKlasseLabel(k.Code, k.Title)))
            ];
        }
    }

    private IReadOnlyList<ModernDropdownOption<Guid?>> KlasseOptions =>
    [
        new(null, "— Choisir une classe —"),
        .. klasseOptions.Select(klasse => new ModernDropdownOption<Guid?>((Guid?)klasse.KlasseId, FormatKlasseLabel(klasse.Code, klasse.Title)))
    ];

    private IReadOnlyList<ExamManagementItem> VisibleExams
    {
        get
        {
            IEnumerable<ExamManagementItem> query = exams;
            if (selectedSchoolFilterId.HasValue)
            {
                query = query.Where(x => x.SchoolId == selectedSchoolFilterId.Value);
            }

            if (selectedKlasseFilterId.HasValue)
            {
                query = query.Where(x => x.KlasseId == selectedKlasseFilterId.Value);
            }

            return query.ToList();
        }
    }

    private IReadOnlyList<ModernDropdownOption<string?>> NiveauOptions =>
    [
        new(string.Empty, "— Choisir une classe —"),
        .. klasseOptions.Select(klasse => new ModernDropdownOption<string?>(klasse.Code, FormatKlasseLabel(klasse.Code, klasse.Title)))
    ];

    private static string FormatKlasseLabel(string? code, string? title)
    {
        var normalizedCode = code?.Trim() ?? string.Empty;
        var normalizedTitle = title?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return normalizedTitle;
        }

        if (string.IsNullOrWhiteSpace(normalizedTitle)
            || string.Equals(normalizedCode, normalizedTitle, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedCode;
        }

        return $"{normalizedCode} — {normalizedTitle}";
    }


    private IReadOnlyList<ModernDropdownOption<string?>> TtsLanguageOptions =>
    [
        new("de-DE", "Allemand (DE)"),
        new("fr-FR", "Français (FR)"),
        new("en-US", "Anglais (EN)")
    ];

    private Task SetNiveauAsync(string? niveauCode)
    {
        if (form is not null)
        {
            form.NiveauCode = niveauCode ?? string.Empty;
            form.KlasseId = klasseOptions.FirstOrDefault(x => string.Equals(x.Code, niveauCode, StringComparison.OrdinalIgnoreCase)).KlasseId;
        }

        return Task.CompletedTask;
    }

    private Task SetKlasseAsync(Guid? klasseId)
    {
        if (form is null)
        {
            return Task.CompletedTask;
        }

        form.KlasseId = klasseId;
        var option = klasseOptions.FirstOrDefault(x => x.KlasseId == klasseId);
        form.NiveauCode = option.KlasseId == Guid.Empty ? string.Empty : option.Code;
        return Task.CompletedTask;
    }

    private Task SetSchoolFilterAsync(Guid? schoolId)
    {
        selectedSchoolFilterId = schoolId;
        if (selectedKlasseFilterId.HasValue && !VisibleExams.Any(x => x.KlasseId == selectedKlasseFilterId.Value))
        {
            selectedKlasseFilterId = null;
        }

        return Task.CompletedTask;
    }

    private Task SetKlasseFilterAsync(Guid? klasseId)
    {
        selectedKlasseFilterId = klasseId;
        return Task.CompletedTask;
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var isPureTeacher = state.User.IsInRole("Lehrer")
                && !state.User.IsInRole("SchulLeiter")
                && !state.User.IsInRole("Koordinator")
                && !state.User.IsInRole("SuperAdmin")
                && !state.User.IsInRole("ProduktOwner");

            if (!Embedded && isPureTeacher)
            {
                Navigation.NavigateTo("/teacher/create?mode=exam", replace: true);
                return;
            }

            Guid.TryParse(state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out currentTeacherId);
            isSchoolDirectionUser = state.User.IsInRole("SchulLeiter") || state.User.IsInRole("Koordinator");
            await LoadAsync();
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private async Task LoadAsync()
    {
        exams = (await ExamService.GetTeacherExamsAsync(currentTeacherId, ComponentToken)).ToList();

        List<Guid> zugeordneteKlassenIds;
        if (isSchoolDirectionUser)
        {
            var schoolIds = await DbContext.UserSchools
                .AsNoTracking()
                .Where(link => link.AppUserId == currentTeacherId)
                .Select(link => link.SchoolId)
                .Distinct()
                .ToListAsync(ComponentToken);

            zugeordneteKlassenIds = schoolIds.Count == 0
                ? []
                : await DbContext.Klassen
                    .AsNoTracking()
                    .Where(klasse => klasse.SchoolId.HasValue && schoolIds.Contains(klasse.SchoolId.Value) && !klasse.IstSystemNiveau)
                    .Select(klasse => klasse.Id)
                    .Distinct()
                    .ToListAsync(ComponentToken);
        }
        else
        {
            zugeordneteKlassenIds = await DbContext.LehrerNiveauZuordnungen
                .AsNoTracking()
                .Where(z => z.LehrerID == currentTeacherId)
                .Select(z => z.KlasseId)
                .Distinct()
                .ToListAsync(ComponentToken);
        }

        var query = DbContext.Klassen
            .AsNoTracking()
            .Where(x => !x.IstSystemNiveau);

        if (zugeordneteKlassenIds.Count > 0)
        {
            query = query.Where(x => zugeordneteKlassenIds.Contains(x.Id));
        }

        klasseOptions = await query
            .OrderBy(x => x.Code)
            .Select(x => new ValueTuple<Guid, string, string, Guid?, string>(
                x.Id,
                x.Code,
                x.Title,
                x.SchoolId,
                x.School != null ? x.School.Name : string.Empty))
            .ToListAsync(ComponentToken);

        schoolOptions = klasseOptions
            .Where(x => x.SchoolId.HasValue)
            .GroupBy(x => new { SchoolId = x.SchoolId!.Value, x.SchoolName })
            .Select(g => (g.Key.SchoolId, g.Key.SchoolName))
            .OrderBy(x => x.SchoolName)
            .ToList();
    }

    private async Task OpenEditor(Guid? examId)
    {
        formError = null;
        if (examId is null)
        {
            form = new ExamFormModel();
            AddExercise();
        }
        else
        {
            form = await ExamService.GetExamForEditAsync(currentTeacherId, examId.Value, ComponentToken) ?? new ExamFormModel();
            if (form.Exercises.Count == 0)
            {
                AddExercise();
            }
        }

        if (isSchoolDirectionUser && form is not null)
        {
            form.ReleaseTarget = ExamReleaseTarget.SchoolLeader;
            form.ShareWithSchoolDirection = true;
            form.AssignToClassStudents = false;
            form.IsPdfExportEnabled = true;
        }
        else if (form is not null && form.ExamId is null)
        {
            form.ReleaseTarget = ExamReleaseTarget.PrivateAuthor;
            form.ShareWithSchoolDirection = false;
            form.AssignToClassStudents = false;
        }

        editorOpen = true;
        await InvokeAsync(async () =>
        {
            await Task.Yield();
            await ScrollModalToTopAsync();
        });
    }

    private string GetExamPreviewHref(Guid examId, bool includeSolutions)
    {
        var from = Embedded ? "teacher-create" : "teacher-exams";
        return $"/teacher-exams/{examId}/view?solutions={(includeSolutions ? "true" : "false")}&from={from}&scope=owner";
    }

    private static string GetAudienceLabel(ExamManagementItem exam)
    {
        if (exam.IsSharedWithSchool && exam.IsPublishedToStudents)
        {
            return "Direction + élèves (score)";
        }

        if (exam.IsPublishedToStudents)
        {
            return "Assigné aux élèves (score)";
        }

        if (exam.IsSharedWithSchool)
        {
            return "Direction / impression (hors score)";
        }

        return "Privé enseignant (hors score)";
    }

    private void CloseEditor()
    {
        editorOpen = false;
        form = null;
    }

    private void AddExercise()
    {
        var exercise = new ExamExerciseFormModel();
        AddQuestion(exercise);
        form!.Exercises.Add(exercise);
    }

    private void RemoveExercise(ExamExerciseFormModel exercise) => form!.Exercises.Remove(exercise);

    private void AddQuestion(ExamExerciseFormModel exercise)
    {
        exercise.Questions.Add(new ExamQuestionFormModel
        {
            TtsLanguage = "de-DE",
            Options =
            [
                new ExamQuestionOptionFormModel { IsCorrect = true },
                new ExamQuestionOptionFormModel()
            ]
        });
    }

    private static Task SetQuestionTtsLanguageAsync(ExamQuestionFormModel question, string? language)
    {
        question.TtsLanguage = language switch
        {
            "fr-FR" => "fr-FR",
            "en-US" => "en-US",
            _ => "de-DE"
        };

        return Task.CompletedTask;
    }

    private void RemoveQuestion(ExamExerciseFormModel exercise, ExamQuestionFormModel question)
        => exercise.Questions.Remove(question);

    private static void SelectSingleCorrectOption(ExamQuestionFormModel question, ExamQuestionOptionFormModel selected)
    {
        foreach (var option in question.Options)
        {
            option.IsCorrect = ReferenceEquals(option, selected);
        }
    }

    private static bool IsAudioType(ExamQuestionType type)
        => type is ExamQuestionType.AudioText or ExamQuestionType.AudioSingleChoice or ExamQuestionType.AudioMultipleChoice;

    private static bool IsChoiceType(ExamQuestionType type)
        => type is ExamQuestionType.SingleChoice or ExamQuestionType.MultipleChoice or ExamQuestionType.AudioSingleChoice or ExamQuestionType.AudioMultipleChoice;

    private async Task ScrollModalToTopAsync()
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("SKOULOUAPPScroll.scrollElementToTop", editorScrollContainer);
            await Task.Delay(220, ComponentToken);
            await JSRuntime.InvokeVoidAsync("SKOULOUAPPScroll.scrollElementToTop", editorScrollContainer);
        }
        catch (JSException)
        {
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async Task SaveExam()
    {
        if (isSavingExam || form is null)
        {
            return;
        }

        formError = null;
        isSavingExam = true;
        try
        {
            if (isSchoolDirectionUser)
            {
                form.ReleaseTarget = ExamReleaseTarget.SchoolLeader;
                form.ShareWithSchoolDirection = true;
                form.IsPdfExportEnabled = true;
            }

            if (!form.ShareWithSchoolDirection && !form.AssignToClassStudents)
            {
                form.ReleaseTarget = ExamReleaseTarget.PrivateAuthor;
            }
            else if (form.AssignToClassStudents)
            {
                form.ReleaseTarget = ExamReleaseTarget.ClassStudents;
            }
            else
            {
                form.ReleaseTarget = ExamReleaseTarget.SchoolLeader;
            }

            await ExamService.SaveExamAsync(currentTeacherId, form, ComponentToken);
            statusMessage = "L'examen a été enregistré.";
            CloseEditor();
            await LoadAsync();
        }
        catch (InvalidOperationException ex)
        {
            formError = ex.Message;
        }
        finally
        {
            isSavingExam = false;
        }
    }

    private async Task DeleteExam(Guid examId)
    {
        var (succeeded, message) = await ExamService.DeleteExamAsync(currentTeacherId, examId, ComponentToken);
        statusMessage = message;
        if (succeeded)
        {
            await LoadAsync();
        }
    }
}
