using HalloApp.Core.DataModels.BaseModel;
using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
namespace HalloApp.Core.DataModels.Courses;

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

    /// <summary>
    /// Gewähltes HALLOAPP-Abo für diese Klasse.
    /// Wird vom SchulLeiter nach der Registrierung des Schülers festgelegt.
    /// </summary>
    public AboTyp AboTyp { get; set; } = AboTyp.HalloApp;

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







