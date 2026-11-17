namespace ENote.Core.DataModels.Formation;

/// <summary>Stand einer Prüfungsrunde (« session d'évaluation ») eines Responsable.</summary>
public enum FormationRundenStatus
{
    /// <summary>Offen: Versuche möglich bis zur Frist.</summary>
    Offen = 0,
    /// <summary>Abgeschlossen, letzter Versuch ≥ Bestehensgrenze.</summary>
    Bestanden = 1,
    /// <summary>Abgeschlossen, letzter Versuch unter der Bestehensgrenze.</summary>
    NichtBestanden = 2,
    /// <summary>Frist abgelaufen ohne abgegebenen Versuch.</summary>
    Verpasst = 3,
    /// <summary>Vom Administrateur erlassen (auch bei einer Pause).</summary>
    Erlassen = 4
}

/// <summary>
/// Allgemeine Regeln der Formation (eine Zeile, Id = 1). Der Administrateur ändert sie in Administration › Formation.
/// Neue Werte gelten für neue Runden; offene Runden behalten ihre Momentaufnahme.
/// </summary>
public class FormationRegel
{
    public int Id { get; set; } = 1;
    public int BestehensGrenzeProzent { get; set; } = 70;
    public int FragenJePruefung { get; set; } = 20;
    public int FristTage { get; set; } = 30;
    public int StandardIntervallMonate { get; set; } = 3;
    public DateTime GeaendertUtc { get; set; }
}

/// <summary>Einstellungen der Formation je Responsable (Intervall, Pause, nächste Runde).</summary>
public class FormationTeilnehmer
{
    public Guid ProduktOwnerId { get; set; }
    public Identity.AppUser ProduktOwner { get; set; } = null!;

    /// <summary>Abstand zwischen zwei Runden in Monaten (vom Administrateur je Person änderbar).</summary>
    public int IntervallMonate { get; set; } = 3;
    /// <summary>Keine Runde bis zu diesem Zeitpunkt (null = keine Pause).</summary>
    public DateTime? PausiertBisUtc { get; set; }
    /// <summary>Ab diesem Zeitpunkt öffnet sich die nächste Runde.</summary>
    public DateTime NaechsteRundeAbUtc { get; set; }
    public DateTime ErstelltUtc { get; set; }
}

/// <summary>Eine vom Responsable gelesene Lektion (Fortschritt im Lernpfad).</summary>
public class FormationLektionGelesen
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProduktOwnerId { get; set; }
    public string LektionId { get; set; } = "";
    public DateTime GelesenUtc { get; set; }
}

/// <summary>
/// Eine Prüfungsrunde: höchstens drei Versuche bis zur Frist, nur der letzte abgegebene Versuch zählt.
/// Grenze und Fragenzahl sind Momentaufnahmen der Regel beim Öffnen.
/// </summary>
public class FormationRunde
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProduktOwnerId { get; set; }
    public Identity.AppUser ProduktOwner { get; set; } = null!;

    public DateTime GeoeffnetUtc { get; set; }
    public DateTime FristUtc { get; set; }
    public DateTime? AbgeschlossenUtc { get; set; }
    public FormationRundenStatus Status { get; set; }

    /// <summary>Endgültige Note = Note des letzten abgegebenen Versuchs (null bei Verpasst/Erlassen ohne Versuch).</summary>
    public int? EndNoteProzent { get; set; }
    public int BestehensGrenzeProzent { get; set; }
    public int FragenAnzahl { get; set; }

    public string? ErlassenGrund { get; set; }
    public Guid? ErlassenVonId { get; set; }

    /// <summary>Mail « session ouverte » an den Responsable verschickt.</summary>
    public DateTime? BenachrichtigtUtc { get; set; }
    /// <summary>Erinnerung kurz vor der Frist verschickt.</summary>
    public DateTime? ErinnerungUtc { get; set; }
    /// <summary>Meldung an das Team bei « non réussie » / « manquée » verschickt.</summary>
    public DateTime? AdminGemeldetUtc { get; set; }

    /// <summary>
    /// Freies Training (Administrateur): keine Frist, beliebig viele Versuche, keine Erinnerungen, zählt nicht
    /// in der Übersicht der Responsables (Nutzer 17.11.: « Die Admins dürfen das auch machen aber ohne Frist und Sperre »).
    /// </summary>
    public bool IstFrei { get; set; }

    public List<FormationVersuch> Versuche { get; set; } = [];

    public const int MaxVersuche = 3;
}

/// <summary>
/// Ein Versuch einer Runde. Die gezogenen Fragen (mit gemischter Antwortreihenfolge) und die Antworten liegen als JSON vor,
/// damit jede Antwort sofort gespeichert wird (schwaches Netz am Handy) und der Versuch später nachvollziehbar bleibt –
/// auch wenn sich der Fragenkatalog im Code ändert.
/// </summary>
public class FormationVersuch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RundeId { get; set; }
    public FormationRunde Runde { get; set; } = null!;

    /// <summary>1 bis 3.</summary>
    public int Nummer { get; set; }
    public DateTime BegonnenUtc { get; set; }
    public DateTime? AbgegebenUtc { get; set; }

    public string FragenJson { get; set; } = "[]";
    public string AntwortenJson { get; set; } = "{}";

    public int FragenAnzahl { get; set; }
    public int? Richtige { get; set; }
    public int? Prozent { get; set; }
}
