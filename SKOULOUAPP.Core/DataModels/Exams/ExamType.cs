namespace HalloApp.Core.DataModels.Exams;

/// <summary>Legt fest, ob ein Exam in die Abschlussnote einer Klasse einfließt.</summary>
public enum ExamType
{
    /// <summary>Reguläres Testexam; zählt nicht zur Abschlussnote.</summary>
    TestExam = 0,

    /// <summary>Exam, dessen bewerteter Versuch in die Abschlussnote einfließt.</summary>
    AbschlussRelevant = 1
}
