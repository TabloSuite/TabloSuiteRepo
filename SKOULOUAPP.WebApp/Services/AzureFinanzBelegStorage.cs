using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using SkoulouApp.Core.Options;
using SkoulouApp.Core.Services.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace SkoulouApp.WebApp.Services;

public sealed class AzureFinanzBelegStorage(IOptions<AzureBlobStorageOptions> options, IWebHostEnvironment environment) : IFinanzBelegStorage
{
    private readonly AzureBlobStorageOptions settings = options.Value;

    private BlobContainerClient Container => SkoulouBlobSpeicher.Container(settings, settings.FinanzBelegContainerName);

    public async Task<string> SpeichernAsync(Guid schulId, Guid belegId, Stream inhalt, string inhaltstyp, CancellationToken cancellationToken = default)
    {
        var blobName = $"schools/{schulId:N}/receipts/{belegId:N}";
        if (!settings.Enabled)
        {
            var path = GetLocalPath(blobName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var created = false;
            try
            {
                await using (var datei = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true;
                    await inhalt.CopyToAsync(datei, cancellationToken);
                }
            }
            catch
            {
                if (created) File.Delete(path);
                throw;
            }
            return blobName;
        }
        ValidateConfiguration();
        var container = Container;
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await container.GetBlobClient(blobName).UploadAsync(inhalt, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = inhaltstyp }
        }, cancellationToken);
        return blobName;
    }

    public async Task<Stream> OeffnenAsync(string blobName, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
            return File.OpenRead(GetLocalPath(blobName));
        ValidateConfiguration();
        var download = await Container.GetBlobClient(blobName).DownloadStreamingAsync(cancellationToken: cancellationToken);
        return download.Value.Content;
    }

    public async Task LoeschenAsync(string blobName, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
        {
            File.Delete(GetLocalPath(blobName));
            return;
        }
        ValidateConfiguration();
        await Container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    private string GetLocalPath(string blobName)
    {
        var teile = blobName.Split('/');
        if (teile.Length != 4 || teile[0] != "schools" || teile[2] != "receipts"
            || !Guid.TryParseExact(teile[1], "N", out _) || !Guid.TryParseExact(teile[3], "N", out _))
            throw new InvalidOperationException("Chemin de justificatif invalide.");

        var root = string.IsNullOrWhiteSpace(settings.LocalDataRootPath)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "media-library")
            : Path.GetFullPath(settings.LocalDataRootPath, environment.ContentRootPath);
        var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        if (Path.GetFullPath(root).StartsWith(webRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFullPath(root), webRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Le stockage privé des justificatifs doit se trouver hors du répertoire public.");

        return Path.Combine(root, "finance-private", teile[1], teile[3]);
    }

    private void ValidateConfiguration()
    {
        if (!SkoulouBlobSpeicher.IstKonfiguriert(settings))
            throw new InvalidOperationException("Le stockage privé des justificatifs n'est pas configuré.");
    }
}
