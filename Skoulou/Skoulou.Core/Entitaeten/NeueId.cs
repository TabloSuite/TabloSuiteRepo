using System.Security.Cryptography;

namespace Skoulou.Core.Entitaeten;

/// <summary>
/// Erzeugt Guids, die SQL Server fortlaufend sortiert (Zeitanteil in den letzten 6 Bytes, wie
/// EF Cores SequentialGuidValueGenerator). So hat jede Entität ihre Id schon beim Anlegen –
/// Verknüpfungen per Id vor dem Speichern sind sicher – und der Index bleibt kompakt.
/// </summary>
public static class NeueId
{
    private static long zaehler = DateTime.UtcNow.Ticks;

    public static Guid Erzeuge()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);

        var ticks = Interlocked.Increment(ref zaehler);
        Span<byte> zeit = stackalloc byte[8];
        BitConverter.TryWriteBytes(zeit, ticks);
        if (!BitConverter.IsLittleEndian) zeit.Reverse();

        // SQL Server vergleicht zuerst die Bytes 10–15: dort steht der Zeitanteil (höchstwertig zuerst).
        bytes[8] = zeit[1];
        bytes[9] = zeit[0];
        bytes[10] = zeit[7];
        bytes[11] = zeit[6];
        bytes[12] = zeit[5];
        bytes[13] = zeit[4];
        bytes[14] = zeit[3];
        bytes[15] = zeit[2];
        return new Guid(bytes);
    }
}
