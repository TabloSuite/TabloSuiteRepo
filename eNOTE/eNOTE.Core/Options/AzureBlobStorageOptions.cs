namespace ENote.Core.Options;

/// <summary>Konfiguration für die Speicherung von Medien (Leistungs-Bilder).</summary>
public class AzureBlobStorageOptions
{
    public const string SectionName = "AzureBlobStorage";

    public bool Enabled { get; set; }
    public string BlobConnectionString { get; set; } = string.Empty;

    /// <summary>Container für die Leistungs-Bilder (projektbezogen).</summary>
    public string ContainerName { get; set; } = "enote-blobuploads";

    /// <summary>Lokaler Ablageort in Development, wenn Blob deaktiviert ist.</summary>
    public string LocalDataRootPath { get; set; } = "wwwroot/media-library";
}
