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

    [SupplyParameterFromQuery(Name = "viewExam")]
    public Guid? ViewExamId { get; set; }

    [SupplyParameterFromQuery(Name = "solutions")]
    public bool? ViewExamWithSolutions { get; set; }

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    private ElementReference editorScrollContainer;

    private List<ExamManagementItem> exams = [];
    private IReadOnlyList<(Guid KlasseId, string Code, string Title)> niveauOptions = [];
    private ExamFormModel? form;
    private bool editorOpen;
    private bool isSavingExam;
    private string? formError;
    private string? statusMessage;
    private Guid currentTeacherId;
    private bool isSchoolDirectionUser;
    private ExamPdfViewModel? pdfPreviewExam;
    private bool pdfPreviewIncludeSolutions;
    private Guid? pdfPreviewExamId;
    private bool examPreviewOpenedFromQuery;

    private IReadOnlyList<ModernDropdownOption<string?>> NiveauOptions =>
    [
        new(string.Empty, "— Choisir —"),
        .. niveauOptions.Select(niveau => new ModernDropdownOption<string?>(niveau.Code, FormatKlasseLabel(niveau.Code, niveau.Title)))
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
        }

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
            await TryOpenExamPreviewFromQueryAsync();
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

        niveauOptions = await query
            .OrderBy(x => x.Code)
            .Select(x => new ValueTuple<Guid, string, string>(x.Id, x.Code, x.Title))
            .ToListAsync(ComponentToken);
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

    private async Task OpenExamPdfPreviewAsync(Guid examId, bool includeSolutions)
    {
        pdfPreviewExam = await ExamService.GetExamForPdfAsync(
            currentTeacherId,
            examId,
            isSchoolDirectionUser,
            ComponentToken);
        pdfPreviewIncludeSolutions = includeSolutions;
        pdfPreviewExamId = pdfPreviewExam is null ? null : examId;
    }

    private async Task TryOpenExamPreviewFromQueryAsync()
    {
        if (examPreviewOpenedFromQuery || !ViewExamId.HasValue)
        {
            return;
        }

        examPreviewOpenedFromQuery = true;

        await OpenExamPdfPreviewAsync(ViewExamId.Value, ViewExamWithSolutions ?? false);

        if (pdfPreviewExam is null)
        {
            formError = "Le contenu de cet examen n'est pas disponible pour votre accès actuel.";
        }
    }

    private void CloseExamPdfPreview()
    {
        pdfPreviewExam = null;
        pdfPreviewExamId = null;
        pdfPreviewIncludeSolutions = false;
    }

    private string GetPdfFileUrl()
    {
        if (!pdfPreviewExamId.HasValue)
        {
            return "#";
        }

        return $"/teacher-exams/{pdfPreviewExamId.Value}/pdf-file?solutions={(pdfPreviewIncludeSolutions ? "true" : "false")}";
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
