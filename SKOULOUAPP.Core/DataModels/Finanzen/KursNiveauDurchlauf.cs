using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;

namespace SkoulouApp.Core.DataModels.Finanzen;

/// <summary>
/// Ein abgegrenzter Zeitraum, in dem ein KursNiveau an einer Schule "läuft".
///
/// Der Durchlauf trennt die Ratenzahlungen sauber pro Kohorte: Ratenzahlungen
/// dürfen nur gebucht werden, solange der Durchlauf aktiv ist (gestartet,
/// <see cref="EndDatum"/> == null). Nach dem Beenden sind keine weiteren Raten
/// mehr möglich, bis das Niveau für die nächsten Schüler erneut gestartet wird
/// (neuer Durchlauf).
///
/// Pro Schule + Niveau darf jeweils nur EIN aktiver Durchlauf existieren.
/// </summary>
public class KursNiveauDurchlauf : AuditableModel
{
    /// <summary>Schule, für die der Durchlauf gilt.</summary>
    public Guid SchulID { get; set; }
    public School Schule { get; set; } = null!;

    /// <summary>Betroffenes KursNiveau.</summary>
    public Guid KlasseId { get; set; }
    public Klasse Klasse { get; set; } = null!;

    /// <summary>
    /// Für diesen Durchlauf eingefrorener Gesamtpreis des Niveaus (FCFA).
    /// Vorbelegt aus Niveau-/Abo-Preis, beim Starten überschreibbar.
    /// </summary>
    public decimal NiveauPreis { get; set; }

    /// <summary>
    /// Eingefrorener Plattformpreis je Schüler für diesen Zyklus. Null kennzeichnet
    /// einen historischen Zyklus vor Einführung der automatischen Abrechnung.
    /// Unabhängig vom Schulgebührenpreis NiveauPreis und vom tatsächlichen Zahlungseingang.
    /// </summary>
    public decimal? PlattformPreisProSchueler { get; set; }

    /// <summary>Auswählbares Startdatum, ab dem Ratenzahlungen erlaubt sind.</summary>
    public DateTime StartDatum { get; set; }

    /// <summary>Auswählbares Enddatum; null = Durchlauf noch aktiv.</summary>
    public DateTime? EndDatum { get; set; }

    /// <summary>Nutzer, der den Durchlauf gestartet hat.</summary>
    public Guid ErstelltVonUserID { get; set; }
    public AppUser? ErstelltVon { get; set; }

    /// <summary>True, solange der Durchlauf nicht beendet wurde.</summary>
    public bool IstAktiv => EndDatum is null;

    public ICollection<RatenZahlung> RatenZahlungen { get; set; } = new List<RatenZahlung>();
}
