namespace SkoulouApp.Core.Models.Media;

using SkoulouApp.Core.DataModels.Lessons;

/// <summary>Darstellung eines auswählbaren Bildes in der Lektionsbibliothek.</summary>
public sealed class BlobMediaAssetItem
{
    public Guid Id { get; init; }
    public Guid? SchoolId { get; init; }
    public string SchoolName { get; init; } = string.Empty;
    public Guid OwnerId { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public Guid? KlasseId { get; init; }
    public string KlasseCode { get; init; } = string.Empty;
    public string AltText { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string OriginalFileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long Length { get; init; }
    public string ImageUrl { get; init; } = string.Empty;
    public BlobMediaType MediaType { get; init; } = BlobMediaType.Image;
    public TimeSpan? VideoDuration { get; init; }
    public string VideoUrl { get; init; } = string.Empty;
}
