namespace ENote.Core.Services.Storage;

public class MediaSaveRequest
{
    public required string DateiName { get; set; }
    public required string ContentType { get; set; }
    public required Stream Inhalt { get; set; }
    public Guid? SalonId { get; set; }
}

public class MediaSaveResult
{
    public bool Erfolg { get; set; }
    public string? Fehler { get; set; }
    public string? BlobName { get; set; }
    public string? OeffentlicheUrl { get; set; }
    public long GroesseBytes { get; set; }

    public static MediaSaveResult Ok(string blobName, string url, long groesse)
        => new() { Erfolg = true, BlobName = blobName, OeffentlicheUrl = url, GroesseBytes = groesse };

    public static MediaSaveResult Fehlgeschlagen(string fehler)
        => new() { Erfolg = false, Fehler = fehler };
}

/// <summary>Speichert und liest Medien (Leistungs-Bilder).</summary>
public interface IMediaStorageService
{
    Task<MediaSaveResult> SaveAsync(MediaSaveRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default);

    /// <summary>Liefert eine abrufbare URL zum Blob.</summary>
    string GetPublicUrl(string blobName);

    /// <summary>Lädt die Bilddaten eines Blobs (z. B. für serverseitige PDF-Erzeugung).</summary>
    Task<byte[]?> LadeBytesAsync(string blobName, CancellationToken cancellationToken = default);
}
