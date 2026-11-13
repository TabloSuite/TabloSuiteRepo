using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Skoulou.WebApp.Services.Infrastruktur.Medien;

/// <summary>
/// Azure-Blob-Speicher: bevorzugt <c>ServiceUri</c> mit Managed Identity, sonst Connection String.
/// Ohne Konfiguration gibt es eine klare Fehlermeldung beim ersten Zugriff (kein stiller Rückfall).
/// </summary>
public sealed class BlobMedienSpeicher(SpeicherOptionen optionen, IConfiguration konfiguration) : IMedienSpeicher
{
    private BlobContainerClient? container;

    /// <summary>Reine Konto-URI (https, ohne Pfad, Query, Port, Benutzerinfo)?</summary>
    public static bool IstGueltigeServiceUri(string? uri)
        => Uri.TryCreate(uri, UriKind.Absolute, out var u)
           && u.Scheme == Uri.UriSchemeHttps
           && string.IsNullOrEmpty(u.Query) && string.IsNullOrEmpty(u.UserInfo)
           && u.IsDefaultPort && (u.AbsolutePath is "/" or "")
           && u.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase);

    private BlobContainerClient Container()
    {
        if (container is not null) return container;
        BlobServiceClient dienst;
        if (!string.IsNullOrWhiteSpace(optionen.ServiceUri))
        {
            if (!IstGueltigeServiceUri(optionen.ServiceUri))
                throw new InvalidOperationException("Storage:ServiceUri doit être https://<compte>.blob.core.windows.net/ (sans SAS ni chemin).");
            dienst = new BlobServiceClient(new Uri(optionen.ServiceUri), new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));
        }
        else
        {
            var verbindung = optionen.BlobConnectionString is { Length: > 0 } cs ? cs
                : konfiguration.GetConnectionString("BlobConnection") is { Length: > 0 } c2 ? c2
                : konfiguration["BlobConnection"];
            if (string.IsNullOrWhiteSpace(verbindung))
                throw new InvalidOperationException("Aucun stockage Blob n'est configuré (Storage:ServiceUri ou Storage:BlobConnectionString).");
            dienst = new BlobServiceClient(verbindung);
        }
        container = dienst.GetBlobContainerClient(optionen.ContainerName);
        return container;
    }

    public async Task<long> SpeichernAsync(string blobName, Stream inhalt, string contentType, CancellationToken ct = default)
    {
        if (!MedienPfad.IstGueltig(blobName)) throw new ArgumentException("Nom de fichier invalide.", nameof(blobName));
        var blob = Container().GetBlobClient(blobName);
        var zaehler = new ZaehlenderStrom(inhalt);
        await blob.UploadAsync(zaehler, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);
        return zaehler.Gelesen;
    }

    public async Task<Stream?> OeffnenAsync(string blobName, CancellationToken ct = default)
    {
        if (!MedienPfad.IstGueltig(blobName)) return null;
        try
        {
            return await Container().GetBlobClient(blobName).OpenReadAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> LoeschenAsync(string blobName, CancellationToken ct = default)
    {
        if (!MedienPfad.IstGueltig(blobName)) return false;
        var antwort = await Container().GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: ct);
        return antwort.Value;
    }

    /// <summary>Zählt die tatsächlich gelesenen Bytes (Länge nie vom Client übernehmen).</summary>
    private sealed class ZaehlenderStrom(Stream inner) : Stream
    {
        public long Gelesen { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Gelesen; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) { var n = inner.Read(buffer, offset, count); Gelesen += n; return n; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { var n = await inner.ReadAsync(buffer, ct); Gelesen += n; return n; }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
