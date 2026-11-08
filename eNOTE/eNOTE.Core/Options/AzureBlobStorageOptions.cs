namespace ENote.Core.Options;

/// <summary>Konfiguration für die Speicherung von Medien (Leistungs-Bilder) in Azure Blob Storage.</summary>
public class AzureBlobStorageOptions
{
    // Hinweis: Der Prefix "AzureBlobStorage" ist in Azure App Service reserviert und wird abgelehnt.
    // Daher wird hier "Storage" verwendet.
    public const string SectionName = "Storage";

    /// <summary>Service-URI des Storage-Accounts (z. B. https://account.blob.core.windows.net).</summary>
    public string ServiceUri { get; set; } = string.Empty;

    /// <summary>Container für die Leistungs-Bilder (projektbezogen).</summary>
    public string ContainerName { get; set; } = "enote-blobuploads";

    /// <summary>Nur in Development verwendeter Ordner außerhalb von wwwroot.</summary>
    public string LocalDataRootPath { get; set; } = "App_Data/media";
}
