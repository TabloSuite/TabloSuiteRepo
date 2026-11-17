namespace Skoulou.Core.Entitaeten;

// Formation der Responsables (Nutzerwunsch 17.11.2026, wie in eNOTE): Lernpfad + Prüfung in Runden.
// Inhalte (Lektionen, Fragen) stehen im Code (Skoulou.WebApp/Services/Formation/FormationKatalog*.cs), hier nur der Stand je Konto.

/// <summary>Stand einer Prüfungsrunde (« session d'évaluation »).</summary>
public enum FormationRundenStatus
{
    Offen = 0,
    Bestanden = 1,
    NichtBestanden = 2,
    Verpasst = 3,
    Erlassen = 4
}

/// <summary>Allgemeine Regeln (eine Zeile). Neue Werte gelten für neue Runden.</summary>
public class FormationRegel : Entitaet
{
    public int BestehensGrenzeProzent { get; set; } = 70;
    public int FragenJePruefung { get; set; } = 20;
    public int FristTage { get; set; } = 30;
    public int StandardIntervallMonate { get; set; } = 3;
}

/// <summary>Einstellungen je Responsable: Intervall, Pause, nächste Runde.</summary>
public class FormationTeilnehmer : Entitaet
{
    public Guid KontoId { get; set; }
    public int IntervallMonate { get; set; } = 3;
    public DateTime? PausiertBisUtc { get; set; }
    public DateTime NaechsteRundeAbUtc { get; set; }
}

/// <summary>Gelesene Lektion (Fortschritt im Lernpfad).</summary>
public class FormationLektionGelesen : Entitaet
{
    public Guid KontoId { get; set; }
    public string LektionId { get; set; } = "";
    public DateTime GelesenUtc { get; set; }
}

/// <summary>
/// Prüfungsrunde: höchstens drei Versuche bis zur Frist, nur der letzte abgegebene zählt. Freies Training der Administrateure:
/// <see cref="IstFrei"/> (ohne Frist, ohne Grenze, nicht in der Übersicht).
/// </summary>
public class FormationRunde : Entitaet
{
    public const int MaxVersuche = 3;

    public Guid KontoId { get; set; }
    public DateTime GeoeffnetUtc { get; set; }
    public DateTime FristUtc { get; set; }
    public DateTime? AbgeschlossenUtc { get; set; }
    public FormationRundenStatus Status { get; set; }
    public int? EndNoteProzent { get; set; }
    public int BestehensGrenzeProzent { get; set; }
    public int FragenAnzahl { get; set; }
    public string? ErlassenGrund { get; set; }
    public Guid? ErlassenVonId { get; set; }
    public bool IstFrei { get; set; }
    public DateTime? BenachrichtigtUtc { get; set; }
    public DateTime? ErinnerungUtc { get; set; }
    public DateTime? AdminGemeldetUtc { get; set; }

    public List<FormationVersuch> Versuche { get; set; } = [];
}

/// <summary>Ein Versuch: gezogene Fragen (mit gemischter Antwortreihenfolge) und Antworten als JSON, jede Antwort sofort gespeichert.</summary>
public class FormationVersuch : Entitaet
{
    public Guid RundeId { get; set; }
    public FormationRunde Runde { get; set; } = null!;
    public int Nummer { get; set; }
    public DateTime BegonnenUtc { get; set; }
    public DateTime? AbgegebenUtc { get; set; }
    public string FragenJson { get; set; } = "[]";
    public string AntwortenJson { get; set; } = "{}";
    public int FragenAnzahl { get; set; }
    public int? Richtige { get; set; }
    public int? Prozent { get; set; }
}
