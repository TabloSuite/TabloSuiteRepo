using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ENote.Core.Options;
using ENote.Core.Services.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ENote.WebApp.Services.Storage;

/// <summary>Azure-Blob-Medienspeicher (Container „enote-blobuploads“) für Produktion.</summary>
public class AzureBlobMediaStorageService(
    IOptions<AzureBlobStorageOptions> options,
    ILogger<AzureBlobMediaStorageService> logger) : IMediaStorageService
{
    private const long MaxGroesseBytes = 8 * 1024 * 1024; // 8 MB

    private BlobContainerClient GetContainer()
    {
        var opts = options.Value;
        var serviceClient = new BlobServiceClient(opts.BlobConnectionString);
        return serviceClient.GetBlobContainerClient(opts.ContainerName);
    }

    public async Task<MediaSaveResult> SaveAsync(MediaSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Inhalt.Length > MaxGroesseBytes)
        {
            return MediaSaveResult.Fehlgeschlagen("L'image dépasse la limite de 8 Mo.");
        }

        var erweiterung = Path.GetExtension(request.DateiName);
        var blobName = $"{(request.SalonId.HasValue ? request.SalonId.ToString() : "global")}/{Guid.NewGuid():N}{erweiterung}";

        try
        {
            var container = GetContainer();
            await container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);
            var blob = container.GetBlobClient(blobName);
            await blob.UploadAsync(request.Inhalt, new BlobHttpHeaders { ContentType = request.ContentType }, cancellationToken: cancellationToken);
            return MediaSaveResult.Ok(blobName, blob.Uri.ToString(), request.Inhalt.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Blob-Upload des Mediums {Datei} fehlgeschlagen", request.DateiName);
            return MediaSaveResult.Fehlgeschlagen("L'enregistrement de l'image a échoué.");
        }
    }

    public async Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        try
        {
            var container = GetContainer();
            return await container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Blob-Löschung von {Blob} fehlgeschlagen", blobName);
            return false;
        }
    }

    public string GetPublicUrl(string blobName)
    {
        var opts = options.Value;
        var basis = opts.BlobConnectionString.Contains("UseDevelopmentStorage=true")
            ? "http://127.0.0.1:10000/devstoreaccount1"
            : string.Empty;

        return string.IsNullOrEmpty(basis)
            ? GetContainer().GetBlobClient(blobName).Uri.ToString()
            : $"{basis}/{opts.ContainerName}/{blobName}";
    }

    public async Task<byte[]?> LadeBytesAsync(string blobName, CancellationToken cancellationToken = default)
    {
        try
        {
            var blob = GetContainer().GetBlobClient(blobName);
            if (!await blob.ExistsAsync(cancellationToken))
            {
                return null;
            }

            var download = await blob.DownloadContentAsync(cancellationToken);
            return download.Value.Content.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Blob-Lesevorgang von {Blob} fehlgeschlagen", blobName);
            return null;
        }
    }
}
