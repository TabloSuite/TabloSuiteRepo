using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Courses;

namespace SkoulouApp.Core.DataModels.Schools;

/// <summary>
/// Repräsentiert einen Abrechnungszeitraum einer Schule.
/// Der SchulLeiter markiert Schüler-Niveaus als bezahlt; der SuperAdmin
/// schließt einen Zeitraum ab, sobald das Geld überwiesen wurde.
/// Nach dem Abschluss beginnt der nächste Zeitraum automatisch am Ende
/// des zuletzt abgeschlossenen Zeitraums.
/// </summary>
public class Abrechnungsperiode : AuditableModel
{
    public Guid SchulID { get; set; }
    public School Schule { get; set; } = null!;

    /// <summary>Beginn des Abrechnungszeitraums (UTC), inklusiv.</summary>
    public DateTime VonUtc { get; set; }

    /// <summary>Ende des Abrechnungszeitraums (UTC), exklusiv.</summary>
    public DateTime BisUtc { get; set; }

    /// <summary>
    /// Summe der abgerechneten Beträge (FCFA) in diesem Zeitraum,
    /// festgeschrieben zum Zeitpunkt des Abschlusses.
    /// </summary>
    public decimal Umsatz { get; set; }

    /// <summary>Anzahl der in diesem Zeitraum abgerechneten Abonnements.</summary>
    public int AnzahlAbonnements { get; set; }

    /// <summary>Gibt an, ob der Zeitraum vom SuperAdmin abgeschlossen wurde.</summary>
    public bool IstAbgeschlossen { get; set; }

    public DateTime? AbgeschlossenAmUtc { get; set; }

    /// <summary>Aktueller Status der Abrechnung im mehrstufigen Workflow.</summary>
    public AbrechnungStatus Status { get; set; } = AbrechnungStatus.Erstellt;

    /// <summary>Benutzer, der die Rechnung erstellt hat; bei Bestandsdaten leer und als SuperAdmin-erstellt behandelt.</summary>
    public Guid? ErstelltVonUserId { get; set; }

    /// <summary>Gibt an, ob die Periode automatisch durch einen Klassenabschluss erzeugt wurde.</summary>
    public bool AutomatischDurchKlassenabschlussErstellt { get; set; }

    /// <summary>Optionaler Bezug zum Klassen-/Kursdurchlauf, der die automatische Abrechnung ausgelöst hat.</summary>
    public Guid? AusgeloestDurchDurchlaufId { get; set; }

    /// <summary>Benutzer, der den Klassenabschluss und damit die automatische Abrechnung bestätigt hat.</summary>
    public Guid? AutomatischAusgeloestVonUserId { get; set; }

    /// <summary>Zeitpunkt der automatischen Erzeugung im Klassenabschluss-Flow.</summary>
    public DateTime? AutomatischErstelltAmUtc { get; set; }

    /// <summary>Markiert eine gezielte Sammelabrechnung aus bereits geschlossenen Klassenzyklen.</summary>
    public bool IstSammelabrechnungAusGeschlossenenKlassen { get; set; }

    /// <summary>Begründung des SchulLeiters bei Ablehnung.</summary>
    public string? AblehnungsGrund { get; set; }

    /// <summary>Zeitpunkt der letzten Reaktion des SchulLeiters (Akzeptiert/Abgelehnt), UTC.</summary>
    public DateTime? SchulLeiterReaktionAmUtc { get; set; }

    /// <summary>Benutzer, der die Zahlung als überwiesen beziehungsweise eingegangen gemeldet hat.</summary>
    public Guid? ZahlungGemeldetVonUserId { get; set; }

    /// <summary>Zeitpunkt der endgültigen Bestätigung durch den SuperAdmin, UTC.</summary>
    public DateTime? BestaetigtAmUtc { get; set; }

    /// <summary>SuperAdmin, der den Bankeingang geprüft und den Vorgang endgültig abgeschlossen hat.</summary>
    public Guid? BestaetigtVonSuperAdminId { get; set; }

    public ICollection<LevelEnrollment> Freischaltungen { get; set; } = new List<LevelEnrollment>();
}
