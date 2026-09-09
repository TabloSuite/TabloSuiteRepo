namespace HalloApp.Core.Options;

/// <summary>
/// Konfiguration für die lokale Medienspeicherung (Bilder/Audio der Lektionen).
/// </summary>
public class MediaStorageOptions
{
    public const string SectionName = "MediaStorage";

    public string RootFolder { get; set; } = "wwwroot/media";
    public string PublicBasePath { get; set; } = "media";
}
