using System.Buffers.Binary;

namespace SkoulouApp.WebApp.Services;

internal static class SchulLogoPruefung
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public static string Pruefen(byte[] daten)
    {
        if (daten.Length == 0 || daten.Length > MaxBytes)
            throw new InvalidOperationException("Le logo doit contenir une image de 2 Mo maximum.");
        int breite = 0, hoehe = 0;
        string typ;
        if (daten.Length >= 24 && daten.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && daten.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            breite = BinaryPrimitives.ReadInt32BigEndian(daten.AsSpan(16, 4));
            hoehe = BinaryPrimitives.ReadInt32BigEndian(daten.AsSpan(20, 4));
            typ = "image/png";
        }
        else if (daten.Length >= 4 && daten[0] == 255 && daten[1] == 216)
        {
            typ = "image/jpeg";
            var position = 2;
            while (position + 3 < daten.Length)
            {
                if (daten[position++] != 255) break;
                while (position < daten.Length && daten[position] == 255) position++;
                if (position >= daten.Length) break;
                var marker = daten[position++];
                if (marker is 0xD9 or 0xDA) break;
                if (marker is 0x01 or >= 0xD0 and <= 0xD7) continue;
                if (position + 2 > daten.Length) break;
                var laenge = BinaryPrimitives.ReadUInt16BigEndian(daten.AsSpan(position, 2));
                if (laenge < 2 || position + laenge > daten.Length) break;
                if (marker is 0xC0 or 0xC1 or 0xC2 && laenge >= 8)
                {
                    hoehe = BinaryPrimitives.ReadUInt16BigEndian(daten.AsSpan(position + 3, 2));
                    breite = BinaryPrimitives.ReadUInt16BigEndian(daten.AsSpan(position + 5, 2));
                    break;
                }
                position += laenge;
            }
        }
        else throw new InvalidOperationException("Choisissez une véritable image PNG ou JPEG.");
        if (breite <= 0 || hoehe <= 0 || breite > 4096 || hoehe > 4096 || (long)breite * hoehe > 8_000_000)
            throw new InvalidOperationException("Le logo est limité à 4 096 pixels par côté et à 8 millions de pixels.");
        try { _ = QuestPDF.Infrastructure.Image.FromBinaryData(daten); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { throw new InvalidOperationException("Cette image est illisible. Choisissez un autre fichier PNG ou JPEG.", ex); }
        return typ;
    }
}
