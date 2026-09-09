using System.Security.Claims;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.Models.Admin;
using HalloApp.Core.Interfaces;
using HalloApp.Core.Services;
using HalloApp.WebApp.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

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

    private List<ExamManagementItem> exams = [];
    private IReadOnlyList<(Guid KlasseId, string Code, string Title)> niveauOptions = [];
    private ExamFormModel? form;
    private bool editorOpen;
    private string? formError;
    private string? statusMessage;
    private Guid currentTeacherId;
    private bool isSchoolDirectionUser;

    private IReadOnlyList<ModernDropdownOption<string?>> NiveauOptions =>
    [
        new(string.Empty, "— Choisir —"),
        .. niveauOptions.Select(niveau => new ModernDropdownOption<string?>(niveau.Code, $"{niveau.Code} — {niveau.Title}"))
    ];

    private IReadOnlyList<ModernDropdownOption<ExamType?>> ExamTypeOptions =>
    [
        new(ExamType.TestExam, "Test — ne compte pas pour la note finale"),
        new(ExamType.AbschlussRelevant, "Examen final — compte pour la note finale")
    ];

    private IReadOnlyList<ModernDropdownOption<string?>> TtsLanguageOptions =>
    [
        new("de-DE", "Deutsch (DE)"),
        new("fr-FR", "Français (FR)"),
        new("en-US", "English (EN)")
    ];

    private Task SetNiveauAsync(string? niveauCode)
    {
        if (form is not null)
        {
            form.NiveauCode = niveauCode ?? string.Empty;
        }

        return Task.CompletedTask;
    }

    private Task SetExamTypeAsync(ExamType? examType)
    {
        if (form is not null && examType is ExamType value)
        {
            form.ExamType = value;
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
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private async Task LoadAsync()
    {
        exams = (await ExamService.GetTeacherExamsAsync(currentTeacherId, ComponentToken)).ToList();

        var zugeordneteKlassenIds = await DbContext.LehrerNiveauZuordnungen
            .AsNoTracking()
            .Where(z => z.LehrerID == currentTeacherId)
            .Select(z => z.KlasseId)
            .Distinct()
            .ToListAsync(ComponentToken);

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
            form.IsPdfExportEnabled = true;
        }

        editorOpen = true;
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

    private async Task SaveExam()
    {
        formError = null;
        try
        {
            if (isSchoolDirectionUser && form is not null)
            {
                form.ReleaseTarget = ExamReleaseTarget.SchoolLeader;
                form.IsPdfExportEnabled = true;
            }

            await ExamService.SaveExamAsync(currentTeacherId, form!, ComponentToken);
            statusMessage = "L'examen a été enregistré.";
            CloseEditor();
            await LoadAsync();
        }
        catch (InvalidOperationException ex)
        {
            formError = ex.Message;
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
