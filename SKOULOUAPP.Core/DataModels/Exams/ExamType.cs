namespace SkoulouApp.Core.DataModels.Exams;

/// <summary>Historischer Examtyp; die Abschlussnote wird unabhängig davon manuell vergeben.</summary>
public enum ExamType
{
    /// <summary>Reguläres Testexam; zählt nicht zur Abschlussnote.</summary>
    TestExam = 0,

    /// <summary>Historischer Typ für bestehende Datensätze; beeinflusst keine Abschlussnote.</summary>
    AbschlussRelevant = 1
}
