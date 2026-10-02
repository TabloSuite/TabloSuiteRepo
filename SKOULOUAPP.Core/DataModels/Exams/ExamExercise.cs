using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels.Exams;

/// <summary>
/// Aufgabenblock innerhalb eines Exams.
/// Ein Exam ist eine Sammlung von Aufgabenblöcken, jeder Block enthält mehrere Aufgaben (ExamQuestion).
/// </summary>
public class ExamExercise : AuditableModel
{
    public Guid ExamId { get; set; }
    public Exam Exam { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string Consigne { get; set; } = string.Empty;
    public int Order { get; set; }

    public ICollection<ExamQuestion> Questions { get; set; } = new List<ExamQuestion>();
}
