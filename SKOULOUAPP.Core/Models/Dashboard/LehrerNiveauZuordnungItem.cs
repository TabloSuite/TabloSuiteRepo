namespace SkoulouApp.Core.Models.Dashboard;

/// <summary>
/// Beschreibt eine mögliche Lehrer-Niveau-Zuordnung innerhalb einer Schule des Direktors
/// und ob sie aktuell aktiv ist. Wird für die Zuordnungs-UI im Direktor-Bereich verwendet.
/// </summary>
public class LehrerNiveauZuordnungItem
{
    public Guid SchulID { get; set; }
    public string SchulName { get; set; } = string.Empty;
    public Guid KlasseId { get; set; }
    public string? ReferenceName { get; set; }
    public string NiveauCode { get; set; } = string.Empty;
    public string NiveauTitel { get; set; } = string.Empty;
    public bool IstZugeordnet { get; set; }
    public decimal Wochenstunden { get; set; } = 1m;
}
