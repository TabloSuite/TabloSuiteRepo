namespace SkoulouApp.Core.Options;

/// <summary>
/// Konfiguration für die Speicherung von Medien in Azure Blob Storage.
/// </summary>
public class AzureBlobStorageOptions
{
    public const string SectionName = "AzureBlobStorage";

    public bool Enabled { get; set; }
    public string BlobConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "etsetech-blobuploads";
    public string FinanzBelegContainerName { get; set; } = "etsetech-blobuploads";
    public string LocalDataRootPath { get; set; } = "wwwroot/media-library";
}
