using SkoulouApp.Core.Models.Media;

namespace SkoulouApp.Core.Services.Storage;

/// <summary>Verwaltet die private, rollen- und schulbezogene Azure-Medienbibliothek.</summary>
public interface IBlobMediaLibraryService
{
    Task<IReadOnlyList<BlobMediaAssetItem>> GetVisibleImagesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BlobMediaAssetItem>> GetVisibleVideosAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default);

    Task<BlobMediaAssetItem> UploadProfileImageAsync(Guid userId, Guid? schoolId, Stream content, string originalFileName, string contentType, CancellationToken cancellationToken = default);

    Task<BlobMediaAssetItem> UploadImageAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        Guid schoolId,
        Guid? klasseId,
        Stream content,
        string originalFileName,
        string displayName,
        string contentType,
        string altText,
        CancellationToken cancellationToken = default);

    Task<BlobMediaDownload?> OpenImageAsync(Guid assetId, Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default);
    Task<BlobMediaDownload?> OpenVideoAsync(Guid assetId, Guid userId, IReadOnlyCollection<string> roles, bool hasActiveSubscription, Guid? lessonId = null, CancellationToken cancellationToken = default);
    Task DeleteImageAsync(Guid assetId, Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default);

    Task<BlobMediaAssetItem> UploadVideoAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        Guid schoolId,
        Guid? klasseId,
        Stream content,
        string originalFileName,
        string displayName,
        string contentType,
        TimeSpan duration,
        CancellationToken cancellationToken = default);
}

/// <summary>Autorisierter, zu streamender Blob-Inhalt.</summary>
public sealed class BlobMediaDownload
{
    public required Stream Content { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
}
