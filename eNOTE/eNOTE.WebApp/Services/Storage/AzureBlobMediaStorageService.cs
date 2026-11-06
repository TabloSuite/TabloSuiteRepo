using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ENote.Core.Options;
using ENote.Core.Services.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ENote.WebApp.Services.Storage;

/// <summary>Azure-Blob-Medienspeicher (Container „enote-blobuploads“).</summary>
public class AzureBlobMediaStorageService(
    BlobServiceClient blobServiceClient,
    IOptions<AzureBlobStorageOptions> options,
    ILogger<AzureBlobMediaStorageService> logger) : IMediaStorageService
{
    private BlobContainerClient GetContainer()
        => blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);

    public async Task<MediaSaveResult> SaveAsync(MediaSaveRequest request, CancellationToken cancellationToken = default)
    {
        var bytes = await MedienDateien.LeseUploadAsync(request.Inhalt, cancellationToken);
        if (bytes is null)
        {
            return MediaSaveResult.Fehlgeschlagen("Le fichier dépasse la limite de 8 Mo.");
        }

        try
        {
            var blobName = MedienDateien.NeuerName(request.SalonId, request.DateiName);
            var container = GetContainer();
            // Container wird separat privat provisioniert. Runtime ändert keine Container-ACLs.
            var blob = container.GetBlobClient(blobName);
            await blob.UploadAsync(
                new BinaryData(bytes),
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = request.ContentType
                    }
                },
                cancellationToken: cancellationToken);

            return MediaSaveResult.Ok(blobName, GetPublicUrl(blobName), bytes.Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Blob-Upload des Mediums {Datei} fehlgeschlagen", request.DateiName);
            return MediaSaveResult.Fehlgeschlagen("L'enregistrement de l'image a échoué.");
        }
    }

    public async Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        if (!MedienDateien.IstGueltigerName(blobName)) return false;
        try
        {
            var container = GetContainer();
            return await container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Blob-Löschung von {Blob} fehlgeschlagen", blobName);
            return false;
        }
    }

    public string GetPublicUrl(string blobName)
        => MedienDateien.Url(blobName);

    public async Task<byte[]?> LadeBytesAsync(string blobName, CancellationToken cancellationToken = default)
    {
        if (!MedienDateien.IstGueltigerName(blobName)) return null;
        try
        {
            var blob = GetContainer().GetBlobClient(blobName);
            if (!(await blob.ExistsAsync(cancellationToken)).Value)
            {
                return null;
            }

            var download = await blob.DownloadContentAsync(cancellationToken);
            return download.Value.Content.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Blob-Lesevorgang von {Blob} fehlgeschlagen", blobName);
            return null;
        }
    }
}
