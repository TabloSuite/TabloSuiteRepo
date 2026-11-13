using System.Buffers.Binary;
using Skoulou.Core.Entitaeten;

namespace Skoulou.Core.Paedagogik;

/// <summary>Am Inhalt (Magic Bytes) erkanntes Dateiformat.</summary>
public sealed record MedienFormat(MedienArt Art, string ContentType, string Endung);

/// <summary>
/// Serverseitige Prüfung hochgeladener Inhaltsmedien (Bauplan §11.20): Bild ≤ 5 MB (PNG, JPEG, GIF, WebP),
/// Video ≤ 300 MB und ≤ 20 min (MP4/MOV). Typ nur über die ersten Bytes, Dauer aus dem MP4-Kopf (mvhd).
/// </summary>
public static class MedienPruefung
{
    public const long MaxBildBytes = 5L * 1024 * 1024;
    public const long MaxVideoBytes = 300L * 1024 * 1024;
    public const int MaxVideoSekunden = 20 * 60;
    public const int KopfLaenge = 16;

    public static long MaxBytes(MedienArt art) => art == MedienArt.Video ? MaxVideoBytes : MaxBildBytes;

    /// <summary>Format aus den ersten Bytes (mindestens 12) oder null.</summary>
    public static MedienFormat? Erkenne(ReadOnlySpan<byte> kopf)
    {
        if (kopf.Length >= 8 && kopf[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return new(MedienArt.Bild, "image/png", ".png");
        if (kopf.Length >= 3 && kopf[0] == 0xFF && kopf[1] == 0xD8 && kopf[2] == 0xFF)
            return new(MedienArt.Bild, "image/jpeg", ".jpg");
        if (kopf.Length >= 6 && kopf[..4].SequenceEqual("GIF8"u8) && (kopf[4] == (byte)'7' || kopf[4] == (byte)'9') && kopf[5] == (byte)'a')
            return new(MedienArt.Bild, "image/gif", ".gif");
        if (kopf.Length >= 12 && kopf[..4].SequenceEqual("RIFF"u8) && kopf[8..12].SequenceEqual("WEBP"u8))
            return new(MedienArt.Bild, "image/webp", ".webp");
        if (kopf.Length >= 12 && kopf[4..8].SequenceEqual("ftyp"u8))
        {
            var marke = kopf[8..12];
            return marke.SequenceEqual("qt  "u8)
                ? new(MedienArt.Video, "video/quicktime", ".mov")
                : new(MedienArt.Video, "video/mp4", ".mp4");
        }
        return null;
    }

    /// <summary>Fehlermeldung zur Größe oder null.</summary>
    public static string? PruefeGroesse(MedienArt art, long bytes)
    {
        if (bytes <= 0) return "Le fichier est vide.";
        var max = MaxBytes(art);
        return bytes > max ? $"Le fichier dépasse la taille autorisée ({max / (1024 * 1024)} Mo au plus)." : null;
    }

    /// <summary>Dauer eines MP4/MOV in Sekunden aus dem « mvhd »-Kopf (auch wenn « moov » am Ende steht); null = unlesbar.</summary>
    public static double? Mp4Dauer(Stream strom)
    {
        if (!strom.CanSeek) return null;
        var moov = SucheBox(strom, 0, strom.Length, "moov");
        if (moov is null) return null;
        var mvhd = SucheBox(strom, moov.Value.Inhalt, moov.Value.Ende, "mvhd");
        if (mvhd is null) return null;

        strom.Position = mvhd.Value.Inhalt;
        Span<byte> puffer = stackalloc byte[32];
        if (strom.ReadAtLeast(puffer[..1], 1, throwOnEndOfStream: false) < 1) return null;
        var version = puffer[0];
        strom.Position = mvhd.Value.Inhalt + 4;
        uint zeitskala;
        ulong dauer;
        if (version == 1)
        {
            if (strom.ReadAtLeast(puffer[..28], 28, throwOnEndOfStream: false) < 28) return null;
            zeitskala = BinaryPrimitives.ReadUInt32BigEndian(puffer[16..20]);
            dauer = BinaryPrimitives.ReadUInt64BigEndian(puffer[20..28]);
        }
        else
        {
            if (strom.ReadAtLeast(puffer[..16], 16, throwOnEndOfStream: false) < 16) return null;
            zeitskala = BinaryPrimitives.ReadUInt32BigEndian(puffer[8..12]);
            dauer = BinaryPrimitives.ReadUInt32BigEndian(puffer[12..16]);
        }
        return zeitskala == 0 ? null : (double)dauer / zeitskala;
    }

    private static (long Inhalt, long Ende)? SucheBox(Stream strom, long von, long bis, string typ)
    {
        Span<byte> kopf = stackalloc byte[16];
        var pos = von;
        for (var schritte = 0; pos + 8 <= bis && schritte < 10_000; schritte++)
        {
            strom.Position = pos;
            if (strom.ReadAtLeast(kopf[..8], 8, throwOnEndOfStream: false) < 8) return null;
            long groesse = BinaryPrimitives.ReadUInt32BigEndian(kopf[..4]);
            var kopfLaenge = 8L;
            if (groesse == 1)
            {
                if (strom.ReadAtLeast(kopf[8..16], 8, throwOnEndOfStream: false) < 8) return null;
                groesse = (long)BinaryPrimitives.ReadUInt64BigEndian(kopf[8..16]);
                kopfLaenge = 16;
            }
            else if (groesse == 0)
            {
                groesse = bis - pos;
            }
            if (groesse < kopfLaenge || pos + groesse > bis) return null;
            if (kopf[4] == (byte)typ[0] && kopf[5] == (byte)typ[1] && kopf[6] == (byte)typ[2] && kopf[7] == (byte)typ[3])
                return (pos + kopfLaenge, pos + groesse);
            pos += groesse;
        }
        return null;
    }
}
