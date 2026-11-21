namespace ENote.Core.DataModels.Infrastruktur;

/// <summary>Ein im Blob-Speicher (Container „enote-blobuploads“) abgelegtes Medium.</summary>
public class BlobMediaAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SalonId { get; set; }

    public string DateiName { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long GroesseBytes { get; set; }

    public Guid HochgeladenVonId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;
}
