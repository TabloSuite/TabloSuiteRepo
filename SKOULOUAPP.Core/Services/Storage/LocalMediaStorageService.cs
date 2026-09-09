using HalloApp.Core.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace HalloApp.Core.Services.Storage;

/// <summary>
/// Speichert Medien im lokalen Dateisystem unterhalb des konfigurierten Ordners.
/// </summary>
public class LocalMediaStorageService(IWebHostEnvironment environment, IOptions<MediaStorageOptions> options) : IMediaStorageService
{
    private readonly MediaStorageOptions storageOptions = options.Value;

    /// <summary>Speichert die Mediendatei und gibt den öffentlichen relativen Pfad zurück.</summary>
    public async Task<MediaSaveResult> SaveAsync(MediaSaveRequest request, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(request.FileName);
        var safeFileName = $"{Guid.NewGuid():N}{extension}";
        var relativeFolder = Path.Combine(storageOptions.PublicBasePath, request.LessonFolder)
            .Replace('\\', '/');
        var physicalFolder = Path.Combine(environment.ContentRootPath, storageOptions.RootFolder, request.LessonFolder);

        Directory.CreateDirectory(physicalFolder);

        var physicalPath = Path.Combine(physicalFolder, safeFileName);
        await using var fileStream = File.Create(physicalPath);
        await request.Content.CopyToAsync(fileStream, cancellationToken);

        return new MediaSaveResult
        {
            RelativePath = $"{relativeFolder}/{safeFileName}",
            StorageProvider = "LocalFileSystem"
        };
    }
}
