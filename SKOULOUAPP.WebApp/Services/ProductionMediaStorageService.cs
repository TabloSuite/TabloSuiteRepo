using HalloApp.Core.Services.Storage;

namespace HalloApp.WebApp.Services;

public sealed class ProductionMediaStorageService : IMediaStorageService
{
    public Task<MediaSaveResult> SaveAsync(MediaSaveRequest request, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Utilisez la bibliothèque de médias pour ajouter des images ou des vidéos.");
}
