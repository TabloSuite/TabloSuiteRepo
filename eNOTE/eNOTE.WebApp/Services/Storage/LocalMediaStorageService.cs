using ENote.Core.Options;
using ENote.Core.Services.Storage;
using Microsoft.Extensions.Options;

namespace ENote.WebApp.Services.Storage;

/// <summary>Private lokale Dateien, ausschließlich in Development.</summary>
public class LocalMediaStorageService : IMediaStorageService
{
    private readonly string wurzelPfad;
    private readonly ILogger<LocalMediaStorageService> logger;

    public LocalMediaStorageService(IOptions<AzureBlobStorageOptions> options, IWebHostEnvironment environment,
        ILogger<LocalMediaStorageService> logger)
    {
        wurzelPfad = EnoteInfrastruktur.LokalerMedienPfad(options.Value, environment);
        this.logger = logger;
    }

    private string? AbsoluterPfad(string blobName)
        => MedienDateien.IstGueltigerName(blobName)
            ? Path.Combine(wurzelPfad, blobName.Replace('/', Path.DirectorySeparatorChar)) : null;

    public async Task<MediaSaveResult> SaveAsync(MediaSaveRequest request, CancellationToken cancellationToken = default)
    {
        var bytes = await MedienDateien.LeseUploadAsync(request.Inhalt, cancellationToken);
        if (bytes is null) return MediaSaveResult.Fehlgeschlagen("Le fichier dépasse la limite de 8 Mo.");
        try
        {
            var name = MedienDateien.NeuerName(request.SalonId, request.DateiName);
            var path = AbsoluterPfad(name)!;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return MediaSaveResult.Ok(name, GetPublicUrl(name), bytes.Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Lokales Speichern des Mediums fehlgeschlagen");
            return MediaSaveResult.Fehlgeschlagen("L'enregistrement du fichier a échoué.");
        }
    }

    public Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = AbsoluterPfad(blobName);
        if (path is null || !File.Exists(path)) return Task.FromResult(false);
        File.Delete(path);
        return Task.FromResult(true);
    }

    public string GetPublicUrl(string blobName) => MedienDateien.Url(blobName);

    public async Task<byte[]?> LadeBytesAsync(string blobName, CancellationToken cancellationToken = default)
    {
        var path = AbsoluterPfad(blobName);
        return path is null || !File.Exists(path) ? null : await File.ReadAllBytesAsync(path, cancellationToken);
    }
}
