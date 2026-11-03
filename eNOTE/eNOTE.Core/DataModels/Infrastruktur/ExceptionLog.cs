namespace ENote.Core.DataModels.Infrastruktur;

/// <summary>Zentrales Exception-Log (Serilog-Sink schreibt hierhin).</summary>
public class ExceptionLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime ZeitUtc { get; set; } = DateTime.UtcNow;
    public string? Nachricht { get; set; }
    public string? Ausnahme { get; set; }
    public string? Pfad { get; set; }
    public Guid? SalonId { get; set; }
    public Guid? BenutzerId { get; set; }
    public bool IstBehandelt { get; set; }
}
