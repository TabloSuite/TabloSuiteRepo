namespace HalloApp.Core.DataModels.Schools;

/// <summary>
/// Status einer Abrechnung im mehrstufigen Workflow zwischen SuperAdmin und SchulLeiter.
/// </summary>
public enum AbrechnungStatus
{
    /// <summary>Vom SuperAdmin erstellt, wartet auf Reaktion des SchulLeiters.</summary>
    Erstellt = 0,

    /// <summary>Vom SchulLeiter akzeptiert und überwiesen, wartet auf Bestätigung des SuperAdmins.</summary>
    Akzeptiert = 1,

    /// <summary>Vom SchulLeiter abgelehnt (mit Begründung).</summary>
    Abgelehnt = 2,

    /// <summary>Vom SuperAdmin endgültig bestätigt und abgeschlossen.</summary>
    Bestaetigt = 3
}
