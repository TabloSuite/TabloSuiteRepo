using System.Security.Cryptography;

namespace ENote.Core.Services.Kasse;

/// <summary>
/// Hashing und Prüfung der kurzen Kassen-PIN (PBKDF2). Die PIN wird nie im
/// Klartext gespeichert.
/// </summary>
public static class KassenPinService
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterationen = 100_000;

    /// <summary>Maximale Fehlversuche, bevor die PIN kurz gesperrt wird.</summary>
    public const int MaxFehlversuche = 5;

    /// <summary>Sperrdauer nach zu vielen Fehlversuchen.</summary>
    public static readonly TimeSpan SperrDauer = TimeSpan.FromMinutes(5);

    public static string HashPin(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterationen, HashAlgorithmName.SHA256, HashBytes);
        return $"{Iterationen}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool PruefePin(string pin, string gespeicherterHash)
    {
        var teile = gespeicherterHash.Split('.');
        if (teile.Length != 3
            || !int.TryParse(teile[0], out var iterationen)
            || iterationen <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(teile[1]);
            var erwartet = Convert.FromBase64String(teile[2]);
            var tatsaechlich = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterationen, HashAlgorithmName.SHA256, erwartet.Length);
            return CryptographicOperations.FixedTimeEquals(tatsaechlich, erwartet);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Eine gültige Kassen-PIN besteht aus 4 bis 6 Ziffern.</summary>
    public static bool IstGueltigesPinFormat(string? pin)
        => !string.IsNullOrEmpty(pin)
           && pin.Length is >= 4 and <= 6
           && pin.All(char.IsDigit);
}
