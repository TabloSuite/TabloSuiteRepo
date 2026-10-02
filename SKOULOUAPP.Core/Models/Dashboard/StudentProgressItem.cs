namespace SkoulouApp.Core.Models.Dashboard;

public class StudentProgressItem
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public Guid SchoolId { get; set; }
    public string SchoolName { get; set; } = string.Empty;
    public string CurrentLevelCode { get; set; } = string.Empty;
    public Guid? CurrentKlasseId { get; set; }

    public bool IsBlocked { get; set; }

    public int CompletedLessonsCount { get; set; }
    public int SolvedExerciseCount { get; set; }
    public int ExamsTaken { get; set; }
    public int ExamsPassed { get; set; }
    public int BestExamScorePercent { get; set; }

    /// <summary>
    /// Kombinierter Fortschritts-Score aus Lektions-Übungen (richtig/falsch) und Prüfungen.
    /// Gilt für ALLE Schüler. Null, wenn noch keine Übung versucht und keine Prüfung abgelegt wurde.
    /// </summary>
    public int? CombinedScorePercent { get; set; }

    /// <summary>Anzahl der Datenpunkte (versuchte Übungen + abgeschlossene Prüfungen) im kombinierten Score.</summary>
    public int CombinedScoreCount { get; set; }

    /// <summary>True, wenn der Schüler für sein aktuelles Niveau ein SkoulouPlus-Abo hat.</summary>
    public bool IsSkoulouPlus { get; set; }

    public DateTime? LastActivityUtc { get; set; }
}
