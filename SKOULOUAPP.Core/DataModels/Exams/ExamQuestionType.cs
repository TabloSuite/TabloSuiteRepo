namespace HalloApp.Core.DataModels.Exams;

public enum ExamQuestionType
{
    SingleChoice = 0,
    MultipleChoice = 1,
    FreeText = 2,

    /// <summary>
    /// Lückentext mit Markern ([[1]], [[2]] ...); akzeptierte Lösungen je Lücke in <see cref="ExamQuestion.GapAnswersJson"/>.
    /// </summary>
    GapFill = 3,

    /// <summary>
    /// Satzbau aus vorgegebenen Wörtern; Musterlösungen in <see cref="ExamQuestion.AcceptedAnswersJson"/>.
    /// </summary>
    SentenceBuilding = 4,

    /// <summary>
    /// Audio-Frage mit reiner Text-Antwort; vorzulesender Text in <see cref="ExamQuestion.AudioText"/>.
    /// </summary>
    AudioText = 5,

    /// <summary>
    /// Audio-Frage mit Einzelauswahl (Radio); vorzulesender Text in <see cref="ExamQuestion.AudioText"/>.
    /// Genau eine Option ist korrekt.
    /// </summary>
    AudioSingleChoice = 6,

    /// <summary>
    /// Audio-Frage mit Mehrfachauswahl (Checkbox); vorzulesender Text in <see cref="ExamQuestion.AudioText"/>.
    /// Mehrere Optionen können korrekt sein.
    /// </summary>
    AudioMultipleChoice = 7
}
