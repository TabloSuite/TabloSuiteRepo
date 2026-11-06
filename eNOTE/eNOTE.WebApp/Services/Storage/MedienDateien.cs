namespace ENote.WebApp.Services.Storage;

public static class MedienDateien
{
    public const int MaxGroesseBytes = 8 * 1024 * 1024;

    public static bool IstGueltigerName(string? blobName)
    {
        if (string.IsNullOrWhiteSpace(blobName)) return false;
        var teile = blobName.Split('/');
        if (teile.Length != 2 || teile[0] != "global" && !Guid.TryParseExact(teile[0], "D", out _)) return false;
        var datei = teile[1];
        var punkt = datei.IndexOf('.');
        var basis = punkt < 0 ? datei : datei[..punkt];
        var endung = punkt < 0 ? "" : datei[(punkt + 1)..];
        return Guid.TryParseExact(basis, "N", out _) && endung.Length <= 10
            && endung.All(char.IsAsciiLetterOrDigit) && (punkt < 0 || endung.Length > 0);
    }

    public static string Url(string blobName)
    {
        if (!IstGueltigerName(blobName)) throw new ArgumentException("Ungültiger Medienname.", nameof(blobName));
        return "/media/" + string.Join('/', blobName.Split('/').Select(Uri.EscapeDataString));
    }

    public static string NeuerName(Guid? salonId, string dateiName)
    {
        var endung = Path.GetExtension(dateiName).ToLowerInvariant();
        var name = $"{(salonId.HasValue ? salonId.Value.ToString("D") : "global")}/{Guid.NewGuid():N}{endung}";
        if (!IstGueltigerName(name)) throw new ArgumentException("Extension de fichier non valide.", nameof(dateiName));
        return name;
    }

    // Auch nicht-seekbare Uploads begrenzen; Stream.Length ist nicht zuverlässig verfügbar.
    public static async Task<byte[]?> LeseUploadAsync(Stream inhalt, CancellationToken ct)
    {
        using var ziel = new MemoryStream();
        var puffer = new byte[81920];
        int gelesen;
        while ((gelesen = await inhalt.ReadAsync(puffer, ct)) > 0)
        {
            if (ziel.Length + gelesen > MaxGroesseBytes) return null;
            await ziel.WriteAsync(puffer.AsMemory(0, gelesen), ct);
        }
        return ziel.ToArray();
    }
}
