using HalloApp.Core.DataModels.BaseModel;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Schools;

namespace HalloApp.Core.DataModels.Lessons;

public enum BlobMediaType
{
    Image = 0,
    Video = 1
}

/// <summary>
/// Metadaten eines privaten Mediums in Azure Blob Storage. Der Blob bleibt privat;
/// die Anwendung entscheidet anhand von Eigentümer und Schule über die Sichtbarkeit.
/// </summary>
public class BlobMediaAsset : AuditableModel
{
    public Guid? SchoolId { get; set; }
    public School? School { get; set; }

    public Guid OwnerId { get; set; }
    public AppUser Owner { get; set; } = null!;

    /// <summary>Optionale Klasse/Lektionsklasse, für die das Bild gedacht ist.</summary>
    public Guid? KlasseId { get; set; }
    public Klasse? Klasse { get; set; }

    public string BlobName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Length { get; set; }
    public string AltText { get; set; } = string.Empty;
    public BlobMediaType MediaType { get; set; } = BlobMediaType.Image;
    public TimeSpan? VideoDuration { get; set; }
}
