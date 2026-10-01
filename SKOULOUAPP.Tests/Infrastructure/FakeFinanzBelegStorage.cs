using HalloApp.Core.Services.Storage;

namespace HalloApp.Tests.Infrastructure;

public sealed class FakeFinanzBelegStorage : IFinanzBelegStorage
{
    public Dictionary<string, byte[]> Dateien { get; } = [];

    public async Task<string> SpeichernAsync(Guid schulId, Guid belegId, Stream inhalt, string inhaltstyp, CancellationToken cancellationToken = default)
    {
        var name = $"schools/{schulId:N}/receipts/{belegId:N}";
        using var buffer = new MemoryStream();
        await inhalt.CopyToAsync(buffer, cancellationToken);
        Dateien[name] = buffer.ToArray();
        return name;
    }

    public Task<Stream> OeffnenAsync(string blobName, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream>(new MemoryStream(Dateien[blobName], writable: false));

    public Task LoeschenAsync(string blobName, CancellationToken cancellationToken = default)
    {
        Dateien.Remove(blobName);
        return Task.CompletedTask;
    }
}
