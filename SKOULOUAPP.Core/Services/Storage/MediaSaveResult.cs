namespace HalloApp.Core.Services.Storage;

/// <summary>
/// Ergebnis einer Medienspeicherung mit relativem Pfad und Speicheranbieter.
/// </summary>
public class MediaSaveResult
{
    public string RelativePath { get; set; } = string.Empty;
    public string StorageProvider { get; set; } = string.Empty;
}
