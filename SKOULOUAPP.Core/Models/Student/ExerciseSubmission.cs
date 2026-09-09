namespace HalloApp.Core.Models.Student;

/// <summary>
/// Antwort-Payload des Schülers für eine einzelne Übung.
/// Je nach Aufgabentyp wird nur das relevante Feld befüllt.
/// </summary>
public class ExerciseSubmission
{
    /// <summary>Bei Choice-/AudioSingleChoice-Aufgaben: gewählte Option (Einzelauswahl).</summary>
    public Guid? SelectedOptionId { get; set; }

    /// <summary>Bei MultipleChoice-/AudioMultipleChoice-Aufgaben: gewählte Optionen (Mehrfachauswahl).</summary>
    public List<Guid>? SelectedOptionIds { get; set; }

    /// <summary>Bei Text-/Satzbau-/Audio-Text-Aufgaben: die Freitextantwort.</summary>
    public string? TextAnswer { get; set; }

    /// <summary>Bei Lückentext-Aufgaben: eingegebene Werte in Marker-Reihenfolge.</summary>
    public List<string>? GapValues { get; set; }
}
