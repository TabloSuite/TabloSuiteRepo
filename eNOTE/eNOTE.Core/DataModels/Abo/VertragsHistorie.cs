namespace ENote.Core.DataModels.Abo;

public class VertragsHistorie
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NutzungsAboId { get; set; }
    public NutzungsAbo NutzungsAbo { get; set; } = null!;
    public Guid BenutzerId { get; set; }
    public DateTime ZeitpunktUtc { get; set; } = DateTime.UtcNow;
    public string Aktion { get; set; } = string.Empty;
    public string? Begruendung { get; set; }
}
