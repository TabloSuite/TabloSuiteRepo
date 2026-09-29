namespace HalloApp.Core.Models.Student;

public class AnswerResult
{
    public bool Succeeded { get; set; }
    public bool IsCorrect { get; set; }
    public Guid CorrectOptionId { get; set; }
    public int AwardedPoints { get; set; }
    public int MaxPoints { get; set; }
    public bool LessonCompleted { get; set; }
    public int CorrectExerciseCount { get; set; }
    public int TotalExerciseCount { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>True, wenn die Antwort mangels Musterlösung manuell benotet werden muss.</summary>
    public bool RequiresManualGrading { get; set; }
}
