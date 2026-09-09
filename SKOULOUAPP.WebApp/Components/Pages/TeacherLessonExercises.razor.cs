using System.Security.Claims;
using HalloApp.Core.Interfaces;
using HalloApp.Core.Models.Admin;
using HalloApp.Core.Models.TeacherLessons;
using HalloApp.Core.Services;
using HalloApp.WebApp.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HalloApp.WebApp.Components.Pages;

public partial class TeacherLessonExercises
{
    [Parameter]
    public bool Embedded { get; set; }

    [Inject]
    private DashboardService DashboardService { get; set; } = default!;

    [Inject]
    private LearningService LearningService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private IHalloAppDbContext DbContext { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private List<LessonEditorListItem> lessons = [];
    private List<KlasseOption> klasses = [];
    private LessonFormModel? form;
    private string selectedLessonCode = string.Empty;

    private bool isLoading = true;
    private bool editorOpen;
    private bool isSaving;
    private bool isSavingManualReview;
    private string? statusMessage;
    private string? errorMessage;
    private List<PendingManualExerciseReviewItem> pendingManualReviews = [];
    private Dictionary<Guid, string> manualFeedbackByAttempt = [];
    private Dictionary<Guid, int> manualPointsByAttempt = [];

    private Guid currentTeacherId;
    private HashSet<Guid> allowedCourseIds = [];

    private sealed class KlasseOption
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    private sealed class LessonEditorListItem
    {
        public Guid LessonId { get; set; }
        public Guid KlasseId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public int ExerciseCount { get; set; }
        public string CourseCode { get; set; } = string.Empty;
    }

    private IReadOnlyList<ModernDropdownOption<Guid?>> CourseOptions =>
    [
        .. klasses.Select(klasse => new ModernDropdownOption<Guid?>((Guid?)klasse.Id, $"{klasse.Code} — {klasse.Title}"))
    ];

    private Task SetLessonCourseAsync(Guid? courseId)
    {
        if (form is null || courseId is not Guid value)
        {
            return Task.CompletedTask;
        }

        form.CourseId = value;
        selectedLessonCode = klasses.FirstOrDefault(klasse => klasse.Id == value)?.Code ?? string.Empty;
        return Task.CompletedTask;
    }

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

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var isPureTeacher = authState.User.IsInRole("Lehrer")
                && !authState.User.IsInRole("SchulLeiter")
                && !authState.User.IsInRole("Koordinator")
                && !authState.User.IsInRole("SuperAdmin")
                && !authState.User.IsInRole("ProduktOwner");

            if (!Embedded && isPureTeacher)
            {
                Navigation.NavigateTo("/teacher/create?mode=lesson", replace: true);
                return;
            }

            var userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userId, out currentTeacherId))
            {
                errorMessage = "Impossible d'identifier votre compte enseignant.";
                isLoading = false;
                return;
            }

            await LoadAsync();
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
        }
    }

    private async Task LoadAsync()
    {
        isLoading = true;
        errorMessage = null;

        allowedCourseIds = await DbContext.LehrerNiveauZuordnungen
            .AsNoTracking()
            .Where(x => x.LehrerID == currentTeacherId)
            .Select(x => x.KlasseId)
            .Distinct()
            .ToHashSetAsync(ComponentToken);

        klasses = await DbContext.Klassen
            .AsNoTracking()
            .Where(klasse => allowedCourseIds.Contains(klasse.Id))
            .OrderBy(klasse => klasse.Code)
            .Select(klasse => new KlasseOption
            {
                Id = klasse.Id,
                Code = klasse.Code,
                Title = klasse.Title
            })
            .ToListAsync(ComponentToken);

        if (allowedCourseIds.Count == 0)
        {
            lessons = [];
            isLoading = false;
            return;
        }

        lessons = await DbContext.Lessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Where(x => allowedCourseIds.Contains(x.KlasseId))
            .OrderBy(x => x.Klasse.Code)
            .ThenBy(x => x.Order)
            .Select(lesson => new LessonEditorListItem
            {
                LessonId = lesson.Id,
                KlasseId = lesson.KlasseId,
                Title = lesson.Title,
                Summary = lesson.Summary,
                ExerciseCount = lesson.Exercises.Count,
                CourseCode = lesson.Klasse.Code
            })
            .ToListAsync(ComponentToken);

        pendingManualReviews = (await LearningService.GetPendingManualExerciseReviewsForTeacherAsync(currentTeacherId, ComponentToken)).ToList();
        manualFeedbackByAttempt = pendingManualReviews.ToDictionary(x => x.AttemptId, _ => string.Empty);
        manualPointsByAttempt = pendingManualReviews.ToDictionary(x => x.AttemptId, x => x.ExercisePoints);

        isLoading = false;
    }

    private async Task OpenEditorAsync(Guid lessonId)
    {
        errorMessage = null;
        statusMessage = null;

        var meta = await DbContext.Lessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Where(x => x.Id == lessonId)
            .Select(x => new { x.KlasseId, x.Klasse.Code })
            .FirstOrDefaultAsync(ComponentToken);

        if (meta is null || !allowedCourseIds.Contains(meta.KlasseId))
        {
            errorMessage = "Cette leçon n'est pas autorisée pour votre profil enseignant.";
            return;
        }

        var loaded = await DashboardService.GetLessonForEditAsync(lessonId, ComponentToken);
        if (loaded is null)
        {
            errorMessage = "Impossible de charger la leçon.";
            return;
        }

        form = loaded;
        if (form.Exercises.Count == 0)
        {
            form.Exercises.Add(CreateDefaultLessonExercise());
            ReorderLessonExercises();
        }

        selectedLessonCode = meta.Code;
        editorOpen = true;
    }

    private async Task OpenCreateAsync()
    {
        errorMessage = null;
        statusMessage = null;

        var firstCourseId = klasses.FirstOrDefault()?.Id ?? Guid.Empty;
        if (firstCourseId == Guid.Empty)
        {
            errorMessage = "Aucune classe n'est attribuée à votre profil.";
            return;
        }

        var maxOrder = await DbContext.Lessons
            .AsNoTracking()
            .Where(lesson => lesson.KlasseId == firstCourseId)
            .MaxAsync(lesson => (int?)lesson.Order, ComponentToken) ?? 0;

        var nextOrder = maxOrder + 1;

        form = new LessonFormModel
        {
            CourseId = firstCourseId,
            Order = nextOrder,
            IsPublished = false
        };
        form.Exercises.Add(CreateDefaultLessonExercise());
        ReorderLessonExercises();

        selectedLessonCode = klasses.FirstOrDefault(klasse => klasse.Id == firstCourseId)?.Code ?? string.Empty;
        editorOpen = true;
    }

    private void CloseEditor()
    {
        editorOpen = false;
        form = null;
        selectedLessonCode = string.Empty;
    }

    private async Task SaveAsync()
    {
        if (form is null)
        {
            return;
        }

        if (!allowedCourseIds.Contains(form.CourseId))
        {
            errorMessage = "Cette classe n'est pas autorisée pour votre profil enseignant.";
            return;
        }

        if (form.LessonId.HasValue)
        {
            var allowed = await DbContext.Lessons
                .AsNoTracking()
                .AnyAsync(x => x.Id == form.LessonId.Value && allowedCourseIds.Contains(x.KlasseId), ComponentToken);

            if (!allowed)
            {
                errorMessage = "Cette leçon n'est pas autorisée pour votre profil enseignant.";
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(form.Content))
        {
            form.Content = "Contenu de leçon (template enseignant).";
        }

        if (string.IsNullOrWhiteSpace(form.Summary))
        {
            form.Summary = form.Title;
        }

        if (string.IsNullOrWhiteSpace(form.Title))
        {
            errorMessage = "Veuillez saisir un titre de leçon.";
            return;
        }

        ReorderLessonExercises();

        isSaving = true;
        errorMessage = null;
        try
        {
            if (form.ExerciseSubmissionDueAtUtc.HasValue)
            {
                form.ExerciseSubmissionDueAtUtc = DateTime.SpecifyKind(form.ExerciseSubmissionDueAtUtc.Value, DateTimeKind.Local).ToUniversalTime();
            }

            var result = await DashboardService.SaveLessonAsync(form, ComponentToken);
            if (!result.Succeeded)
            {
                errorMessage = result.Message;
                return;
            }

            statusMessage = "Les exercices de la leçon ont été enregistrés.";
            editorOpen = false;
            form = null;
            selectedLessonCode = string.Empty;
            await LoadAsync();
        }
        finally
        {
            isSaving = false;
        }
    }

    private async Task GradeManualAsync(Guid attemptId)
    {
        isSavingManualReview = true;
        errorMessage = null;
        try
        {
            if (!manualPointsByAttempt.TryGetValue(attemptId, out var manualPoints))
            {
                errorMessage = "Points manuels introuvables.";
                return;
            }

            var result = await LearningService.GradeManualExerciseAttemptAsync(
                currentTeacherId,
                attemptId,
                manualPoints,
                manualFeedbackByAttempt.TryGetValue(attemptId, out var feedback) ? feedback : null,
                ComponentToken);

            if (!result.Succeeded)
            {
                errorMessage = result.Message;
                return;
            }

            manualFeedbackByAttempt.Remove(attemptId);
            manualPointsByAttempt.Remove(attemptId);
            statusMessage = result.Message;
            pendingManualReviews = pendingManualReviews.Where(x => x.AttemptId != attemptId).ToList();
        }
        finally
        {
            isSavingManualReview = false;
        }
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
        form?.Exercises.Add(CreateDefaultLessonExercise());
        ReorderLessonExercises();
    }

    private void RemoveLessonExercise(LessonExerciseFormModel exercise)
    {
        form?.Exercises.Remove(exercise);
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
        if (form is null)
        {
            return;
        }

        for (var i = 0; i < form.Exercises.Count; i++)
        {
            form.Exercises[i].Order = i + 1;
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
}