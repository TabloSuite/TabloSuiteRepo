using ENote.Core.Options;
using ENote.Core.Services.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ENote.WebApp.Services.Storage;

/// <summary>Lokaler Medienspeicher für Development (konfigurierbarer Ordner, auch außerhalb der App).</summary>
public class LocalMediaStorageService(
    IOptions<AzureBlobStorageOptions> options,
    IWebHostEnvironment environment,
    ILogger<LocalMediaStorageService> logger) : IMediaStorageService
{
    private const long MaxGroesseBytes = 8 * 1024 * 1024; // 8 MB

    /// <summary>Absoluter Wurzelpfad (unterstützt absolute und ContentRoot-relative Pfade).</summary>
    private string WurzelPfad
    {
        get
        {
            var konfiguriert = options.Value.LocalDataRootPath;
            return Path.IsPathRooted(konfiguriert)
                ? konfiguriert
                : Path.Combine(environment.ContentRootPath, konfiguriert);
        }
    }

    private string AbsoluterPfad(string blobName)
        => Path.Combine(WurzelPfad, blobName.Replace('/', Path.DirectorySeparatorChar));

    public async Task<MediaSaveResult> SaveAsync(MediaSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Inhalt.Length > MaxGroesseBytes)
        {
            return MediaSaveResult.Fehlgeschlagen("L'image dépasse la limite de 8 Mo.");
        }

        var erweiterung = Path.GetExtension(request.DateiName);
        var blobName = $"{(request.SalonId.HasValue ? request.SalonId.ToString() : "global")}/{Guid.NewGuid():N}{erweiterung}";
        var zielPfad = AbsoluterPfad(blobName);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(zielPfad)!);
            await using var datei = File.Create(zielPfad);
            await request.Inhalt.CopyToAsync(datei, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lokales Speichern des Mediums {Datei} fehlgeschlagen", request.DateiName);
            return MediaSaveResult.Fehlgeschlagen("L'enregistrement de l'image a échoué.");
        }

        return MediaSaveResult.Ok(blobName, GetPublicUrl(blobName), request.Inhalt.Length);
    }

    public Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        var pfad = AbsoluterPfad(blobName);
        if (File.Exists(pfad))
        {
            File.Delete(pfad);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>Lokale Dateien werden über den geschützten Endpunkt /media ausgeliefert.</summary>
    public string GetPublicUrl(string blobName)
        => "/media/" + Uri.EscapeDataString(blobName);

    /// <summary>Liefert den absoluten Dateipfad (für den /media-Endpunkt) oder null, wenn ungültig.</summary>
    public string? ResolveAbsoluterPfad(string blobName)
    {
        if (string.IsNullOrWhiteSpace(blobName) || blobName.Contains(".."))
        {
            return null;
        }

        var pfad = AbsoluterPfad(blobName);
        return File.Exists(pfad) ? pfad : null;
    }

    public async Task<byte[]?> LadeBytesAsync(string blobName, CancellationToken cancellationToken = default)
    {
        var pfad = ResolveAbsoluterPfad(blobName);
        return pfad is null ? null : await File.ReadAllBytesAsync(pfad, cancellationToken);
    }
}

