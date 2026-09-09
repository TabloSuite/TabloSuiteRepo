using HalloApp.Core.Services.Storage;

namespace HalloApp.Tests.Infrastructure;

/// <summary>
/// Test-Double für <see cref="IMediaStorageService"/>. Führt keine Datei-E/A aus,
/// sondern liefert einen festen relativen Pfad und Anbieter zurück.
/// </summary>
public class FakeMediaStorageService : IMediaStorageService
{
    public Task<MediaSaveResult> SaveAsync(MediaSaveRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new MediaSaveResult
        {
            RelativePath = $"media/{request.LessonFolder}/{request.FileName}",
            StorageProvider = "Fake"
        });
    }
}
