namespace HalloApp.Core.Models.Dashboard;

/// <summary>
/// Beschreibt eine mögliche Lehrer-Fach-Zuordnung innerhalb einer Schule des Direktors
/// und ob sie aktuell aktiv ist.
/// </summary>
public class LehrerFachZuordnungItem
{
    public Guid SchulID { get; set; }
    public string SchulName { get; set; } = string.Empty;
    public Guid FachId { get; set; }
    public string FachName { get; set; } = string.Empty;
    public bool IstZugeordnet { get; set; }
}
