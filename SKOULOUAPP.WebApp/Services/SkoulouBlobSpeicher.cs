using Azure.Identity;
using Azure.Storage.Blobs;
using SkoulouApp.Core.Options;

namespace SkoulouApp.WebApp.Services;

/// <summary>
/// Container-Zugriff für Medien und Belege. Bevorzugt wie eNOTE: gemeinsames TabloSuite-Speicherkonto über
/// <see cref="AzureBlobStorageOptions.ServiceUri"/> mit systemseitiger Managed Identity (kein Schlüssel),
/// SKOULOU in eigenen Containern. Übergangsweise weiter möglich: Verbindungszeichenfolge.
/// </summary>
public static class SkoulouBlobSpeicher
{
    public static bool IstKonfiguriert(AzureBlobStorageOptions options)
        => !string.IsNullOrWhiteSpace(options.ServiceUri) || !string.IsNullOrWhiteSpace(options.BlobConnectionString);

    public static BlobContainerClient Container(AzureBlobStorageOptions options, string containerName)
    {
        if (!string.IsNullOrWhiteSpace(options.ServiceUri))
        {
            var basis = PruefeServiceUri(options.ServiceUri);
            return new BlobContainerClient(new Uri(basis, containerName), new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));
        }

        return new BlobContainerClient(options.BlobConnectionString, containerName);
    }

    /// <summary>Nur eine reine HTTPS-Konto-URI ohne SAS, Pfad oder Port (wie eNOTE).</summary>
    public static Uri PruefeServiceUri(string serviceUri)
    {
        if (!Uri.TryCreate(serviceUri.TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/" || !uri.IsDefaultPort
            || !uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage:ServiceUri doit être l'URI HTTPS du compte de stockage (sans SAS).");
        }

        return uri;
    }
}
