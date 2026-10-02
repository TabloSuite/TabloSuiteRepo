namespace SkoulouApp.Core.Models.Student;

public class ExamResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public int CorrectAnswers { get; set; }
    public int TotalQuestions { get; set; }
    public int ScorePercent { get; set; }
    public int PassingScore { get; set; }
    public bool IsPassed { get; set; }
    public bool TimedOut { get; set; }

    /// <summary>
    /// True, wenn Text-Antworten ohne Musterlösung noch manuell benotet werden müssen.
    /// Das Endergebnis kann sich nach der Korrektur durch die Lehrkraft noch ändern.
    /// </summary>
    public bool RequiresManualGrading { get; set; }
}
