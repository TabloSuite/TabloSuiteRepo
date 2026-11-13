using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur.Medien;

namespace Skoulou.WebApp.Services.Organisation;

/// <summary>
/// Hochgeladene Bilder (Logo, Schülerfoto): Größe serverseitig gezählt (höchstens 5 MB, Bauplan §11.20),
/// Typ nur über Magic Bytes (PNG, JPEG, WebP) – Dateiname und Content-Type des Browsers zählen nicht.
/// </summary>
public static class BildDatei
{
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>Content-Type aus den ersten Bytes; null = kein erlaubtes Bild.</summary>
    public static string? Erkenne(ReadOnlySpan<byte> kopf)
    {
        if (kopf.Length >= 8 && kopf[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (kopf.Length >= 3 && kopf[0] == 0xFF && kopf[1] == 0xD8 && kopf[2] == 0xFF) return "image/jpeg";
        if (kopf.Length >= 12 && kopf[..4].SequenceEqual("RIFF"u8) && kopf[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    /// <summary>Liest höchstens <see cref="MaxBytes"/> und prüft den Typ; sonst <see cref="FachFehler"/>.</summary>
    public static async Task<(byte[] Inhalt, string ContentType)> LeseAsync(Stream quelle, CancellationToken ct)
    {
        using var puffer = new MemoryStream();
        var block = new byte[81920];
        int n;
        while ((n = await quelle.ReadAsync(block, ct)) > 0)
        {
            puffer.Write(block, 0, n);
            if (puffer.Length > MaxBytes) throw new FachFehler("L'image est trop lourde (5 Mo au maximum).");
        }
        if (puffer.Length == 0) throw new FachFehler("Le fichier est vide.");
        var inhalt = puffer.ToArray();
        var typ = Erkenne(inhalt) ?? throw new FachFehler("Seules les images PNG, JPEG ou WebP sont acceptées.");
        return (inhalt, typ);
    }

    /// <summary>Speichert das Bild als neues Medium (gespeichert wird vom Aufrufer).</summary>
    public static async Task<Medium> SpeichereAsync(SkoulouDbContext db, IMedienSpeicher speicher, Guid schuleId, Guid besitzerId,
        MedienZweck zweck, byte[] inhalt, string contentType, string? dateiname, CancellationToken ct)
    {
        var medium = new Medium
        {
            SchuleId = schuleId, BesitzerId = besitzerId, Art = MedienArt.Bild, Zweck = zweck, ContentType = contentType,
            Dateiname = Path.GetFileName(dateiname ?? "image") is { Length: > 0 and <= 200 } d ? d : "image"
        };
        medium.BlobName = MedienPfad.Fuer(schuleId, zweck, medium.Id);
        using var strom = new MemoryStream(inhalt, writable: false);
        medium.Bytes = await speicher.SpeichernAsync(medium.BlobName, strom, contentType, ct);
        db.Medien.Add(medium);
        return medium;
    }

    /// <summary>Bild eines Mediums lesen (für PDFs); null, wenn nicht vorhanden.</summary>
    public static async Task<byte[]?> LadeAsync(SkoulouDbContext db, IMedienSpeicher speicher, Guid? mediumId, CancellationToken ct)
    {
        if (mediumId is not { } id) return null;
        var blob = await db.Medien.AsNoTracking().Where(m => m.Id == id && m.Art == MedienArt.Bild)
            .Select(m => m.BlobName).FirstOrDefaultAsync(ct);
        if (blob is null) return null;
        try
        {
            await using var strom = await speicher.OeffnenAsync(blob, ct);
            if (strom is null) return null;
            using var ziel = new MemoryStream();
            await strom.CopyToAsync(ziel, ct);
            return ziel.Length is > 0 and <= MaxBytes ? ziel.ToArray() : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
