using HalloApp.Core.DataModels.BaseModel;

namespace HalloApp.Core.DataModels.Finanzen;

public class FinanzBeleg : AuditableModel
{
    public Guid BuchungID { get; set; }
    public FinanzBuchung Buchung { get; set; } = null!;
    public Guid SchulID { get; set; }
    public string Dateiname { get; set; } = string.Empty;
    public string Inhaltstyp { get; set; } = string.Empty;
    public string? BlobName { get; set; }
    public byte[] Inhalt { get; set; } = [];
}
