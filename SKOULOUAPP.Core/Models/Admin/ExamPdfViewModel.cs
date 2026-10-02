namespace SkoulouApp.Core.Models.Admin;

/// <summary>
/// Projektion einer Prüfung für den druckbaren Prüfungsbogen (PDF), optional mit Lösungen.
/// </summary>
public class ExamPdfViewModel
{
    public Guid ExamId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string NiveauCode { get; set; } = string.Empty;
    public int DurationInMinutes { get; set; }
    public int TotalPoints { get; set; }
    public List<ExamPdfExercise> Exercises { get; set; } = [];
}

public class ExamPdfExercise
{
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Consigne { get; set; } = string.Empty;
    public List<ExamPdfQuestion> Questions { get; set; } = [];
}

public class ExamPdfQuestion
{
    public int Number { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public int Points { get; set; }

    /// <summary>Bewertungstyp als Kürzel (Choice, GapFill, Text, ...), steuert die Darstellung.</summary>
    public string QuestionType { get; set; } = string.Empty;

    /// <summary>Antwortoptionen (nur Text, ohne Kennzeichnung der richtigen Antwort).</summary>
    public List<string> Options { get; set; } = [];

    public List<string> CorrectOptions { get; set; } = [];

    public string AcceptedAnswers { get; set; } = string.Empty;

    public List<string> GapSolutions { get; set; } = [];

    /// <summary>Anzahl der Lücken bei GapFill-Aufgaben (für Antwortfelder auf dem Bogen).</summary>
    public int GapCount { get; set; }
}
