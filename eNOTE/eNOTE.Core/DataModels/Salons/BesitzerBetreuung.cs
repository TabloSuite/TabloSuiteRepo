namespace ENote.Core.DataModels.Salons;

public class BesitzerBetreuung
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BesitzerId { get; set; }
    public Guid ProduktOwnerId { get; set; }
    public DateTime GueltigAbUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;
    public DateTime? GueltigBisUtc { get; set; }
}
