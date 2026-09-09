namespace HalloApp.Core.Models.Student;

/// <summary>
/// Antwort eines Schülers auf eine einzelne Prüfungsfrage.
/// Je nach Fragetyp wird nur das relevante Feld befüllt.
/// </summary>
public class ExamAnswerSubmission
{
    public Guid QuestionId { get; set; }

    /// <summary>Bei Choice-/AudioSingleChoice-Fragen: gewählte Option (Einzelauswahl).</summary>
    public Guid? SelectedOptionId { get; set; }

    /// <summary>Bei MultipleChoice-/AudioMultipleChoice-Fragen: gewählte Optionen (Mehrfachauswahl).</summary>
    public List<Guid>? SelectedOptionIds { get; set; }

    /// <summary>Bei Text-/Satzbau-/Audio-Text-Fragen: die Freitextantwort.</summary>
    public string? TextAnswer { get; set; }

    /// <summary>Bei GapFill: eingegebene Werte in Marker-Reihenfolge.</summary>
    public List<string>? GapValues { get; set; }
}
