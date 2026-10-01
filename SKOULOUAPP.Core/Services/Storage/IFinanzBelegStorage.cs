namespace HalloApp.Core.Services.Storage;

public interface IFinanzBelegStorage
{
    Task<string> SpeichernAsync(Guid schulId, Guid belegId, Stream inhalt, string inhaltstyp, CancellationToken cancellationToken = default);
    Task<Stream> OeffnenAsync(string blobName, CancellationToken cancellationToken = default);
    Task LoeschenAsync(string blobName, CancellationToken cancellationToken = default);
}
