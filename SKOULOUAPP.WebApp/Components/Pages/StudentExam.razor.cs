using System.Security.Claims;
using System.Threading;
using HalloApp.Core.Models.Student;
using HalloApp.Core.Services;
using HalloApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace HalloApp.WebApp.Components.Pages;

public partial class StudentExam : CancelableComponentBase
{
    [Inject]
    private ExamService ExamService { get; set; } = default!;

    [Inject]
    private ITextToSpeechProvider TextToSpeech { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private DatabaseActionGuard DatabaseActionGuard { get; set; } = default!;

    [Parameter]
    public Guid ExamId { get; set; }

    private ExamSessionViewModel? session;
    private ExamResult? result;
    private bool notFound;
    private bool isSubmitting;
    private Guid studentId;
    private int remainingSeconds;
    private int totalSeconds;
    private Timer? timer;
    private int currentQuestionIndex;

    private readonly Dictionary<Guid, Guid> answers = [];
    private readonly Dictionary<Guid, HashSet<Guid>> multiAnswers = [];
    private readonly Dictionary<Guid, string> textAnswers = [];
    private readonly Dictionary<Guid, List<string>> gapAnswers = [];

    private int answeredCount => answers.Count
        + multiAnswers.Count(x => x.Value.Count > 0)
        + textAnswers.Count(x => !string.IsNullOrWhiteSpace(x.Value))
        + gapAnswers.Count(x => x.Value.Any(v => !string.IsNullOrWhiteSpace(v)));
    private string FormattedTime => TimeSpan.FromSeconds(Math.Max(0, remainingSeconds)).ToString(@"mm\:ss");
    private int TimerRingPercent => totalSeconds > 0 ? (int)Math.Round(100.0 * Math.Max(0, remainingSeconds) / totalSeconds) : 0;
    private string TimerRingColor => remainingSeconds <= 30 ? "#e11d48" : remainingSeconds <= 120 ? "#d97706" : "#059669";

    private ExamQuestionViewModel? CurrentQuestion
        => session is not null && currentQuestionIndex >= 0 && currentQuestionIndex < session.Questions.Count
            ? session.Questions[currentQuestionIndex]
            : null;

    private bool IsFirstQuestion => currentQuestionIndex <= 0;

    private bool IsLastQuestion => session is null || currentQuestionIndex >= session.Questions.Count - 1;

    private void GoToQuestion(int index)
    {
        if (session is null || index < 0 || index >= session.Questions.Count)
        {
            return;
        }

        currentQuestionIndex = index;
    }

    private void GoToPreviousQuestion() => GoToQuestion(currentQuestionIndex - 1);

    private void GoToNextQuestion() => GoToQuestion(currentQuestionIndex + 1);

    private bool IsQuestionAnswered(ExamQuestionViewModel question)
    {
        if (question.QuestionType == "GapFill")
        {
            return gapAnswers.TryGetValue(question.QuestionId, out var list) && list.Any(v => !string.IsNullOrWhiteSpace(v));
        }

        if (IsMultipleChoiceType(question.QuestionType))
        {
            return multiAnswers.TryGetValue(question.QuestionId, out var set) && set.Count > 0;
        }

        if (IsChoiceType(question.QuestionType))
        {
            return answers.ContainsKey(question.QuestionId);
        }

        return textAnswers.TryGetValue(question.QuestionId, out var text) && !string.IsNullOrWhiteSpace(text);
    }

    private string GetQuestionDotClass(int index, ExamQuestionViewModel question)
    {
        const string baseClass = "exercise-dot h-2.5 w-2.5 rounded-full transition duration-150 ";

        if (IsQuestionAnswered(question))
        {
            return baseClass + "bg-emerald-500";
        }

        if (index == currentQuestionIndex)
        {
            return baseClass + "bg-amber-600 scale-125";
        }

        return baseClass + "bg-slate-200 hover:bg-slate-300";
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

            session = await ExamService.StartExamAsync(ExamId, studentId, ComponentToken);
            if (session is null)
            {
                notFound = true;
                return;
            }

            remainingSeconds = (int)Math.Max(0, (session.DeadlineUtc - DateTime.UtcNow).TotalSeconds);
            totalSeconds = remainingSeconds;
            timer = new Timer(OnTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private async void OnTick(object? state)
    {
        remainingSeconds--;
        if (remainingSeconds <= 0)
        {
            remainingSeconds = 0;
            await InvokeAsync(async () => await SubmitAsync(true));
            return;
        }

        await InvokeAsync(StateHasChanged);
    }

    private void Select(Guid questionId, Guid optionId)
    {
        if (result is not null)
        {
            return;
        }

        answers[questionId] = optionId;
    }

    private void ToggleOption(Guid questionId, Guid optionId)
    {
        if (result is not null)
        {
            return;
        }

        if (!multiAnswers.TryGetValue(questionId, out var set))
        {
            set = [];
            multiAnswers[questionId] = set;
        }

        if (!set.Remove(optionId))
        {
            set.Add(optionId);
        }
    }

    private bool IsOptionSelected(Guid questionId, Guid optionId)
        => multiAnswers.TryGetValue(questionId, out var set) && set.Contains(optionId);

    private void SetTextAnswer(Guid questionId, string? value)
    {
        if (result is not null)
        {
            return;
        }

        textAnswers[questionId] = value ?? string.Empty;
    }

    private string GetTextAnswer(Guid questionId)
        => textAnswers.TryGetValue(questionId, out var value) ? value : string.Empty;

    private void SetGapValue(Guid questionId, int gapCount, int index, string? value)
    {
        if (result is not null)
        {
            return;
        }

        if (!gapAnswers.TryGetValue(questionId, out var list))
        {
            list = Enumerable.Repeat(string.Empty, gapCount).ToList();
            gapAnswers[questionId] = list;
        }

        while (list.Count < gapCount)
        {
            list.Add(string.Empty);
        }

        if (index >= 0 && index < list.Count)
        {
            list[index] = value ?? string.Empty;
        }
    }

    private string GetGapValue(Guid questionId, int index)
        => gapAnswers.TryGetValue(questionId, out var list) && index >= 0 && index < list.Count
            ? list[index]
            : string.Empty;

    private async Task PlayAudioAsync(string text)
    {
        await TextToSpeech.SpeakAsync(text);
    }

    private static bool IsChoiceType(string questionType)
        => questionType is "Choice" or "SingleChoice" or "MultipleChoice" or "AudioChoice" or "AudioSingleChoice" or "AudioMultipleChoice";

    private static bool IsMultipleChoiceType(string questionType)
        => questionType is "MultipleChoice" or "AudioMultipleChoice";

    private static bool IsAudioType(string questionType)
        => questionType is "AudioText" or "AudioChoice" or "AudioSingleChoice" or "AudioMultipleChoice";

    private async Task SubmitAsync(bool auto)
    {
        if (isSubmitting || result is not null || session is null)
        {
            return;
        }

        isSubmitting = true;
        timer?.Dispose();
        timer = null;

        try
        {
            await DatabaseActionGuard.RunAsync(async () =>
            {
                var submissions = session.Questions.Select(q => new ExamAnswerSubmission
                {
                    QuestionId = q.QuestionId,
                    SelectedOptionId = answers.TryGetValue(q.QuestionId, out var opt) ? opt : null,
                    SelectedOptionIds = multiAnswers.TryGetValue(q.QuestionId, out var multi) ? multi.ToList() : null,
                    TextAnswer = textAnswers.TryGetValue(q.QuestionId, out var txt) ? txt : null,
                    GapValues = gapAnswers.TryGetValue(q.QuestionId, out var gaps) ? gaps : null
                }).ToList();

                result = await ExamService.SubmitExamAsync(session.AttemptId, studentId, submissions);
            });
        }
        finally
        {
            isSubmitting = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    public override void Dispose()
    {
        timer?.Dispose();
        base.Dispose();
    }
}
