using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
namespace SkoulouApp.Core.DataModels.Courses;

/// <summary>
/// Repräsentiert die Freischaltung eines Niveaus (Klasse) für einen Schüler.
/// Freischaltung (IsUnlocked) und Bezahlstatus (IsPaid) sind unabhängig voneinander:
/// der Lehrer kann ein Niveau freischalten, ohne es sofort als bezahlt zu markieren,
/// und umgekehrt.
/// </summary>
public class LevelEnrollment : AuditableModel
{
    public Guid StudentId { get; set; }
    public AppUser Student { get; set; } = null!;

    public Guid KlasseId { get; set; }
    public Klasse Klasse { get; set; } = null!;

    public Guid? DurchlaufId { get; set; }
    public KursNiveauDurchlauf? Durchlauf { get; set; }
    public bool IstAktuell { get; set; } = true;
    /// <summary>Nachvollziehbare Ausnahme für eine fehlerhafte Zykluszuordnung, keine Löschung.</summary>
    public DateTime? StorniertAmUtc { get; set; }
    public Guid? StorniertVonID { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(300)]
    public string? StornoGrund { get; set; }

    /// <summary>
    /// Gewähltes SKOULOUAPP-Abo für diese Klasse.
    /// Wird vom SchulLeiter nach der Registrierung des Schülers festgelegt.
    /// </summary>
    public AboTyp AboTyp { get; set; } = AboTyp.SkoulouApp;

    /// <summary>
    /// Steuert, ob der Schüler auf die Lektionen dieses Niveaus zugreifen darf.
    /// </summary>
    public bool IsUnlocked { get; set; }

    /// <summary>
    /// Gibt an, ob die Schule das Geld für dieses Niveau bereits an den Betreiber
    /// weitergegeben hat.
    /// </summary>
    public bool IsPaid { get; set; }

    /// <summary>
    /// Preis zum Zeitpunkt der Freischaltung, damit spätere Preisänderungen
    /// bestehende Schulden nicht nachträglich verändern.
    /// </summary>
    public decimal PriceAtEnrollment { get; set; }

    /// <summary>Beim Aboabschluss eingefrorener Kommissionsanteil des Schulleiters.</summary>
    public decimal SchoolLeaderCommissionAtEnrollment { get; set; }

    /// <summary>Beim Aboabschluss eingefrorener Kommissionsanteil des Lehrers.</summary>
    public decimal TeacherCommissionAtEnrollment { get; set; }

    /// <summary>Beim Aboabschluss eingefrorener Kommissionsanteil des Product Owners.</summary>
    public decimal ProductOwnerCommissionAtEnrollment { get; set; }

    public DateTime? UnlockedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }

    /// <summary>
    /// Abrechnungsperiode, in der die Bezahlung dieses Niveaus final
    /// abgeschlossen (an den Betreiber überwiesen) wurde. Null, solange
    /// die Zahlung noch nicht in einer abgeschlossenen Periode enthalten ist.
    /// </summary>
    public Guid? AbgerechnetInPeriodeId { get; set; }
    public Abrechnungsperiode? AbgerechnetInPeriode { get; set; }
}







