namespace Skoulou.WebApp.Services.Infrastruktur.Medien;

/// <summary>
/// Lokaler Speicher für Development unter <c>Storage:LocalDataRootPath</c>; der Ordner darf nicht
/// unter wwwroot liegen (sonst wären private Dateien öffentlich abrufbar).
/// </summary>
public sealed class LokalerMedienSpeicher : IMedienSpeicher
{
    private readonly string wurzel;

    public LokalerMedienSpeicher(string wurzel, string webRoot)
    {
        var voll = Path.GetFullPath(wurzel);
        var web = Path.GetFullPath(webRoot);
        if (voll.StartsWith(web, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Le dossier de stockage local ne doit pas se trouver sous wwwroot.");
        this.wurzel = voll;
    }

    public string Wurzel => wurzel;

    private string Pfad(string blobName)
    {
        if (!MedienPfad.IstGueltig(blobName)) throw new ArgumentException("Nom de fichier invalide.", nameof(blobName));
        var pfad = Path.GetFullPath(Path.Combine(wurzel, blobName.Replace('/', Path.DirectorySeparatorChar)));
        if (!pfad.StartsWith(wurzel, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Chemin hors du dossier de stockage.", nameof(blobName));
        return pfad;
    }

    public async Task<long> SpeichernAsync(string blobName, Stream inhalt, string contentType, CancellationToken ct = default)
    {
        var pfad = Pfad(blobName);
        Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
        await using var ziel = new FileStream(pfad, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await inhalt.CopyToAsync(ziel, ct);
        return ziel.Length;
    }

    public Task<Stream?> OeffnenAsync(string blobName, CancellationToken ct = default)
    {
        var pfad = Pfad(blobName);
        Stream? strom = File.Exists(pfad)
            ? new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(strom);
    }

    public Task<bool> LoeschenAsync(string blobName, CancellationToken ct = default)
    {
        var pfad = Pfad(blobName);
        if (!File.Exists(pfad)) return Task.FromResult(false);
        File.Delete(pfad);
        return Task.FromResult(true);
    }
}
