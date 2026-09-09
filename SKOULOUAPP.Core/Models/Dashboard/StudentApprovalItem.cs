namespace HalloApp.Core.Models.Dashboard;

public class StudentApprovalItem
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string SchoolName { get; set; } = string.Empty;
    public Guid SchoolId { get; set; }
    public string CurrentLevelCode { get; set; } = string.Empty;
    public Guid? CurrentKlasseId { get; set; }
    public string CurrentKlasseName { get; set; } = string.Empty;

    public bool IsBlocked { get; set; }
    public bool IsSelfRegistered { get; set; }
    public int CompletedLessonsCount { get; set; }
    public DateTime EnrolledAtUtc { get; set; }

    /// <summary>
    /// Gibt an, ob der Schüler mindestens ein bezahltes Niveau besitzt.
    /// Ist dies der Fall, darf der Schulleiter den Schüler nicht mehr blockieren
    /// (nur der SuperAdmin darf dies rückgängig machen).
    /// </summary>
    public bool HasPaidEnrollment { get; set; }

    /// <summary>True, wenn der Schüler für sein aktuelles Niveau ein HalloPlus-Abo hat.</summary>
    public bool IsHalloPlus { get; set; }

    /// <summary>True, wenn mindestens eine aktive (freigeschaltete) Abo-Zuordnung existiert.</summary>
    public bool HasActiveSubscription { get; set; }

    /// <summary>Zu zahlende Schul-/Klassengebühr für den aktuellen Durchlauf.</summary>
    public decimal? SchoolFeeAmount { get; set; }

    /// <summary>Bereits bezahlter Anteil der Schul-/Klassengebühr für den aktuellen Durchlauf.</summary>
    public decimal SchoolFeePaidAmount { get; set; }

    /// <summary>True, wenn für die aktuelle Klasse ein Finanzdurchlauf gefunden wurde.</summary>
    public bool HasActiveSchoolFeeCycle { get; set; }

    public bool HasSchoolFeeInfo => SchoolFeeAmount.HasValue || CurrentKlasseId.HasValue;

    public bool IsSchoolFeeFullyPaid => SchoolFeeAmount.HasValue && SchoolFeePaidAmount >= SchoolFeeAmount.Value;

    /// <summary>
    /// Kombinierter Fortschritts-Score aus Lektions-Übungen und Prüfungen (für ALLE Schüler).
    /// </summary>
    public int? CombinedScorePercent { get; set; }

    /// <summary>Anzahl der Datenpunkte (versuchte Übungen + Prüfungen) im kombinierten Score.</summary>
    public int CombinedScoreCount { get; set; }

}
