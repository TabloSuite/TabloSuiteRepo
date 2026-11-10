namespace ENote.Core.DataModels.Abo;

public class MonatsPreisStand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SalonId { get; set; }
    public DateTime GueltigAbUtc { get; set; }
    public decimal? MonatsPreis { get; set; }
    /// <summary>Gehört zu einer (geplanten) Preisanpassung – wird beim Aufheben mit entfernt.</summary>
    public Guid? PreisAnpassungId { get; set; }
}
