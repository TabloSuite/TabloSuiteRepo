namespace SkoulouApp.Core.Models.Exams;

/// <summary>Abschlussübersicht einer Klasse innerhalb eines konkreten Kursdurchlaufs.</summary>
public class KlassenAbschlussUebersicht
{
    public Guid DurchlaufId { get; set; }
    public Guid KlasseId { get; set; }
    public Guid SchulID { get; set; }
    public string KlasseCode { get; set; } = string.Empty;
    public bool IstAktiv { get; set; }
    public List<KlassenAbschlussSchueler> Schueler { get; set; } = [];
}

/// <summary>Abschlussstatus eines Schülers inklusive manuell gepflegter Abschlussnote.</summary>
public class KlassenAbschlussSchueler
{
    public Guid StudentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AbschlussNoteText { get; set; }
    public string? MentionText { get; set; }
    public int AbschlussExamAnzahl { get; set; }
    public bool? IsBestanden { get; set; }
}

/// <summary>Ein nachtragbares Ergebnis eines abschlussrelevanten Examversuchs.</summary>
public class AbschlussExamErgebnis
{
    public Guid AttemptId { get; set; }
    public Guid StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string ExamTitel { get; set; } = string.Empty;
    public int AutomatischerScorePercent { get; set; }
    public decimal? ManuelleNote { get; set; }
    public decimal WirksameNote => ManuelleNote ?? AutomatischerScorePercent;
}
