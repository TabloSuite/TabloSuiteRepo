namespace HalloApp.Core.Models.Admin;

using HalloApp.Core.DataModels.Exams;

/// <summary>
/// Übersichtseintrag einer vom Lehrer erstellten Prüfung (Exam) für die Verwaltung.
/// </summary>
public class ExamManagementItem
{
    public Guid ExamId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string NiveauCode { get; set; } = string.Empty;
    public int DurationInMinutes { get; set; }
    public int PassingScore { get; set; }
    public int ExerciseCount { get; set; }
    public int QuestionCount { get; set; }
    public int AttemptCount { get; set; }
    public ExamType ExamType { get; set; }
    public ExamReleaseTarget ReleaseTarget { get; set; }
}
