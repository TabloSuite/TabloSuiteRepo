namespace HalloApp.Core.Services.Storage;

/// <summary>
/// Dienst zum Speichern von Medien (Bilder/Audio) für Lektionen.
/// </summary>
public interface IMediaStorageService
{
    /// <summary>Speichert eine Mediendatei und liefert deren relativen Pfad.</summary>
    Task<MediaSaveResult> SaveAsync(MediaSaveRequest request, CancellationToken cancellationToken = default);
}
