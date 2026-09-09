namespace HalloApp.Core.Models.Admin;

public class UserManagementItem
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool IsBlocked { get; set; }
    public bool IsSelfRegistered { get; set; }
    public List<string> Schools { get; set; } = [];

    /// <summary>Gibt an, ob der Schüler mindestens ein bezahltes Niveau besitzt.</summary>
    public bool HasPaidEnrollment { get; set; }

    /// <summary>True, wenn der Schüler für sein aktuelles Niveau ein HalloPlus-Abo hat.</summary>
    public bool IsHalloPlus { get; set; }

    /// <summary>Zu zahlende Schul-/Klassengebühr für den aktuellen Durchlauf.</summary>
    public decimal? SchoolFeeAmount { get; set; }

    /// <summary>Bereits bezahlter Anteil der Schul-/Klassengebühr für den aktuellen Durchlauf.</summary>
    public decimal SchoolFeePaidAmount { get; set; }

    /// <summary>True, wenn für die aktuelle Klasse ein Finanzdurchlauf gefunden wurde.</summary>
    public bool HasActiveSchoolFeeCycle { get; set; }

    public bool HasSchoolFeeInfo => SchoolFeeAmount.HasValue || HasStudentClass;

    public bool IsSchoolFeeFullyPaid => SchoolFeeAmount.HasValue && SchoolFeePaidAmount >= SchoolFeeAmount.Value;

    /// <summary>True, wenn der Schüler eine aktuelle Klasse besitzt.</summary>
    public bool HasStudentClass { get; set; }

    /// <summary>
    /// Kombinierter Fortschritts-Score aus Lektions-Übungen und Prüfungen (für ALLE Schüler).
    /// </summary>
    public int? CombinedScorePercent { get; set; }

    /// <summary>Anzahl der Datenpunkte (versuchte Übungen + Prüfungen) im kombinierten Score.</summary>
    public int CombinedScoreCount { get; set; }

}
