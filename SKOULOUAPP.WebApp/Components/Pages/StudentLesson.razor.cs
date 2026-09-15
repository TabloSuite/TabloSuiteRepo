using System.Security.Claims;
using HalloApp.Core.Interfaces;
using HalloApp.Core.Models.Student;
using HalloApp.Core.Services;
using HalloApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace HalloApp.WebApp.Components.Pages;

public partial class StudentLesson
{
    [Inject]
    private LearningService LearningService { get; set; } = default!;

    [Inject]
    private ITextToSpeechProvider TextToSpeech { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IHalloAppDbContext DbContext { get; set; } = default!;

    [Inject]
    private IJSRuntime JsRuntime { get; set; } = default!;

    [Inject]
    private DatabaseActionGuard DatabaseActionGuard { get; set; } = default!;

    [Parameter]
    public Guid LessonId { get; set; }

    private LessonDetailViewModel? lesson;
    private bool notFound;
    private Guid studentId;
    private string studentFirstName = string.Empty;
    private int correctCount;
    private bool exerciseStarted;
    private bool audioActivated;
    private bool audioActivationFailed;
    private string? audioDiagnosticMessage;
    private int currentExerciseIndex;

    private readonly Dictionary<Guid, ExerciseState> exerciseStates = [];
    private List<ExerciseDisplayItem> exerciseDisplayItems = [];

    private int LessonPercent => lesson is null || lesson.Exercises.Count == 0
        ? 0
        : (int)Math.Round(correctCount * 100.0 / lesson.Exercises.Count);

    private int SolvedCount => correctCount;

    private List<ExerciseViewModel> OrderedExercises
        => lesson?.Exercises.OrderBy(e => e.Order).ToList() ?? [];

    private ExerciseDisplayItem? CurrentExerciseDisplay
        => currentExerciseIndex >= 0 && currentExerciseIndex < exerciseDisplayItems.Count
            ? exerciseDisplayItems[currentExerciseIndex]
            : null;

    private int TotalExerciseBlocks => exerciseDisplayItems.Select(item => item.BlockIndex).Distinct().Count();

    private string? GetLessonAudioUrl()
        => lesson?.Media.FirstOrDefault(media => string.Equals(media.MediaType, "Audio", StringComparison.OrdinalIgnoreCase))?.Url;

    private string? LessonVideoUrl
        => lesson?.Media.FirstOrDefault(media => string.Equals(media.MediaType, "video", StringComparison.OrdinalIgnoreCase))?.Url;

    private bool IsFirstExercise => currentExerciseIndex <= 0;

    private bool IsLastExercise => currentExerciseIndex >= exerciseDisplayItems.Count - 1;

    private void GoToExercise(int index)
    {
        if (index < 0 || index >= exerciseDisplayItems.Count)
        {
            return;
        }

        currentExerciseIndex = index;
    }

    private void GoToPreviousExercise() => GoToExercise(currentExerciseIndex - 1);

    private void GoToNextExercise() => GoToExercise(currentExerciseIndex + 1);

    private sealed class ExerciseState
    {
        public Guid? SelectedOptionId { get; set; }
        public List<Guid> SelectedOptionIds { get; set; } = [];
        public bool IsCorrect { get; set; }
        public bool IsSubmitting { get; set; }
        public string? Feedback { get; set; }
        public Guid? AnsweredCorrectOptionId { get; set; }
        public string? TextAnswer { get; set; }
        public List<string> GapValues { get; set; } = [];
        public bool RequiresManualGrading { get; set; }
        public bool IsGradedManually { get; set; }
    }

    private sealed class ExerciseDisplayItem
    {
        public required ExerciseViewModel Exercise { get; init; }
        public required int BlockIndex { get; init; }
        public required int QuestionIndex { get; init; }
        public required int QuestionCount { get; init; }
        public required string BlockTitle { get; init; }
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var userIdValue = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdValue, out studentId))
            {
                notFound = true;
                return;
            }

            studentFirstName = await DbContext.Users
                .AsNoTracking()
                .Where(user => user.Id == studentId)
                .Select(user => user.Vorname)
                .FirstOrDefaultAsync(ComponentToken) ?? string.Empty;

            lesson = await LearningService.GetLessonDetailAsync(LessonId, studentId, ComponentToken);
            if (lesson is null)
            {
                notFound = true;
                return;
            }

            foreach (var exercise in lesson.Exercises)
            {
                exerciseStates[exercise.ExerciseId] = new ExerciseState
                {
                    IsCorrect = exercise.IsAnsweredCorrectly,
                    SelectedOptionId = exercise.SelectedOptionId,
                    TextAnswer = exercise.SubmittedTextAnswer,
                    GapValues = exercise.SubmittedGapValues.Count > 0
                        ? [.. exercise.SubmittedGapValues]
                        : Enumerable.Repeat(string.Empty, exercise.GapCount).ToList(),
                    Feedback = exercise.IsAnsweredCorrectly ? "Richtig!" : null
                };
            }

            RebuildExerciseDisplayItems();

            RecalculateCorrectCount();

            exerciseStarted = lesson.IsCompleted || correctCount > 0;
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private void StartExercises() => exerciseStarted = true;

    private void SelectOption(Guid exerciseId, Guid optionId)
    {
        var state = exerciseStates[exerciseId];
        if (state.IsCorrect)
        {
            return;
        }

        state.SelectedOptionId = optionId;
        state.Feedback = null;
    }

    private void ToggleOption(Guid exerciseId, Guid optionId)
    {
        var state = exerciseStates[exerciseId];
        if (state.IsCorrect)
        {
            return;
        }

        if (!state.SelectedOptionIds.Remove(optionId))
        {
            state.SelectedOptionIds.Add(optionId);
        }

        state.Feedback = null;
    }

    private async Task SubmitAsync(Guid exerciseId)
    {
        var state = exerciseStates[exerciseId];
        var exercise = lesson?.Exercises.FirstOrDefault(e => e.ExerciseId == exerciseId);
        if (exercise is null || state.IsSubmitting || !CanSubmit(exercise, state))
        {
            return;
        }

        state.IsSubmitting = true;
        try
        {
            await DatabaseActionGuard.RunAsync(async () =>
            {
                var submission = new ExerciseSubmission
                {
                    SelectedOptionId = IsChoiceType(exercise.ExerciseType) && !IsMultipleChoiceType(exercise.ExerciseType) ? state.SelectedOptionId : null,
                    SelectedOptionIds = IsMultipleChoiceType(exercise.ExerciseType) ? state.SelectedOptionIds : null,
                    TextAnswer = state.TextAnswer,
                    GapValues = exercise.ExerciseType == "GapFill" ? state.GapValues : null
                };

                var result = await LearningService.SubmitExerciseAsync(exerciseId, studentId, submission);
                state.Feedback = result.Message;
                state.IsCorrect = result.IsCorrect;
                state.RequiresManualGrading = result.RequiresManualGrading;
                state.IsGradedManually = result.RequiresManualGrading;
                if (result.IsCorrect)
                {
                    state.AnsweredCorrectOptionId = result.CorrectOptionId;
                }

                if (lesson is not null)
                {
                    lesson.IsCompleted = result.LessonCompleted;
                }

                RecalculateCorrectCount();
                await PlayExerciseFeedbackAsync(result.IsCorrect, result.RequiresManualGrading);

                if (result.IsCorrect && !IsLastExercise)
                {
                    _ = InvokeAsync(async () =>
                    {
                        await Task.Delay(700);
                        GoToNextExercise();
                        StateHasChanged();
                    });
                }
            });
        }
        finally
        {
            state.IsSubmitting = false;
        }
    }

    private async Task PlayExerciseFeedbackAsync(bool isCorrect, bool requiresManualGrading)
    {
        try
        {
            await JsRuntime.InvokeVoidAsync(
                "halloStudentExerciseFeedback.play",
                LessonPercent,
                isCorrect,
                requiresManualGrading,
                string.IsNullOrWhiteSpace(studentFirstName) ? null : studentFirstName,
                !IsLastExercise);
        }
        catch (JSException)
        {
            // Audio feedback is optional and must never block the exercise flow.
        }
    }

    private void SetTextAnswer(Guid exerciseId, string? value)
    {
        var state = exerciseStates[exerciseId];
        if (state.IsCorrect)
        {
            return;
        }

        state.TextAnswer = value;
        state.Feedback = null;
    }

    private void SetGapValue(Guid exerciseId, int gapCount, int index, string? value)
    {
        var state = exerciseStates[exerciseId];
        if (state.IsCorrect)
        {
            return;
        }

        while (state.GapValues.Count < gapCount)
        {
            state.GapValues.Add(string.Empty);
        }

        if (index >= 0 && index < state.GapValues.Count)
        {
            state.GapValues[index] = value ?? string.Empty;
        }

        state.Feedback = null;
    }

    private static string GetGapValue(ExerciseState state, int index)
        => index >= 0 && index < state.GapValues.Count ? state.GapValues[index] : string.Empty;

    private async Task PlayAudioAsync(string text)
    {
        await TextToSpeech.SpeakAsync(text);
    }

    private async Task ActivateAudioAsync()
    {
        try
        {
            audioActivated = await JsRuntime.InvokeAsync<bool>("SKOULOUAPPAudio.activate");
            audioActivationFailed = !audioActivated;
            await UpdateAudioDiagnosticAsync();
        }
        catch (JSException)
        {
            audioActivated = false;
            audioActivationFailed = true;
            audioDiagnosticMessage = "Audio-Diagnose konnte nicht gelesen werden.";
        }
    }

    private async Task TestAudioToneAsync()
    {
        try
        {
            audioActivated = await JsRuntime.InvokeAsync<bool>("SKOULOUAPPAudio.testTone");
            audioActivationFailed = !audioActivated;
            await UpdateAudioDiagnosticAsync();
        }
        catch (JSException)
        {
            audioActivationFailed = true;
            audioDiagnosticMessage = "Testton konnte nicht gestartet werden.";
        }
    }

    private async Task UpdateAudioDiagnosticAsync()
    {
        var status = await JsRuntime.InvokeAsync<AudioStatus>("SKOULOUAPPAudio.getStatus");
        var ttsStatus = await JsRuntime.InvokeAsync<string>("SKOULOUAPPTts.getStatus");
        audioDiagnosticMessage = $"AudioContext: {status.AudioContextState}, TTS: {(status.SpeechSynthesisSupported ? "oui" : "non")}, voix: {status.SpeechSynthesisVoices}, dernier statut: {ttsStatus}";
    }

    private sealed class AudioStatus
    {
        public bool AudioContextSupported { get; set; }
        public string AudioContextState { get; set; } = string.Empty;
        public bool Unlocked { get; set; }
        public bool SpeechSynthesisSupported { get; set; }
        public int SpeechSynthesisVoices { get; set; }
        public string UserAgent { get; set; } = string.Empty;
    }

    private static bool IsChoiceType(string exerciseType)
        => exerciseType is "Choice" or "SingleChoice" or "MultipleChoice" or "AudioChoice" or "AudioSingleChoice" or "AudioMultipleChoice";

    private static bool IsMultipleChoiceType(string exerciseType)
        => exerciseType is "MultipleChoice" or "AudioMultipleChoice";

    private static bool IsAudioType(string exerciseType)
        => exerciseType is "AudioText" or "AudioChoice" or "AudioSingleChoice" or "AudioMultipleChoice";

    private static bool CanSubmit(ExerciseViewModel exercise, ExerciseState state)
    {
        if (IsMultipleChoiceType(exercise.ExerciseType))
        {
            return state.SelectedOptionIds.Count > 0;
        }

        if (IsChoiceType(exercise.ExerciseType))
        {
            return state.SelectedOptionId is not null;
        }

        if (exercise.ExerciseType == "GapFill")
        {
            return state.GapValues.Any(v => !string.IsNullOrWhiteSpace(v));
        }

        return !string.IsNullOrWhiteSpace(state.TextAnswer);
    }

    private void RecalculateCorrectCount()
        => correctCount = exerciseStates.Values.Count(x => x.IsCorrect);

    private string GetDotClass(int index, ExerciseState state)
    {
        const string baseClass = "exercise-dot h-2.5 w-2.5 rounded-full transition duration-150 ";

        if (state.IsCorrect)
        {
            return baseClass + "bg-emerald-500";
        }

        if (index == currentExerciseIndex)
        {
            return baseClass + "bg-indigo-600 scale-125";
        }

        return baseClass + "bg-slate-200 hover:bg-slate-300";
    }

    private static string GetOptionClass(ExerciseState state, bool isSelected, bool isCorrectOption)
    {
        const string baseClass = "w-full rounded-2xl border px-4 py-3 text-left text-base font-medium transition duration-150 ";

        if (state.IsCorrect && isCorrectOption)
        {
            return baseClass + "border-emerald-300 bg-emerald-50 text-emerald-800";
        }

        if (isSelected)
        {
            return baseClass + "border-indigo-400 bg-indigo-50 text-indigo-800";
        }

        return baseClass + "border-slate-200 bg-white text-slate-700 hover:border-indigo-200 hover:bg-slate-50";
    }

    private void RebuildExerciseDisplayItems()
    {
        exerciseDisplayItems = [];
        var ordered = OrderedExercises;
        if (ordered.Count == 0)
        {
            return;
        }

        var blocks = new List<(string Title, List<ExerciseViewModel> Questions)>();
        foreach (var exercise in ordered)
        {
            var title = string.IsNullOrWhiteSpace(exercise.Title) ? "Exercice" : exercise.Title.Trim();
            if (blocks.Count == 0 || !string.Equals(blocks[^1].Title, title, StringComparison.OrdinalIgnoreCase))
            {
                blocks.Add((title, [exercise]));
            }
            else
            {
                blocks[^1].Questions.Add(exercise);
            }
        }

        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            var block = blocks[blockIndex];
            for (var questionIndex = 0; questionIndex < block.Questions.Count; questionIndex++)
            {
                exerciseDisplayItems.Add(new ExerciseDisplayItem
                {
                    Exercise = block.Questions[questionIndex],
                    BlockIndex = blockIndex + 1,
                    QuestionIndex = questionIndex + 1,
                    QuestionCount = block.Questions.Count,
                    BlockTitle = block.Title
                });
            }
        }
    }
}
