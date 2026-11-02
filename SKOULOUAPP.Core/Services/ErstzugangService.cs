using System.Security.Cryptography;

namespace SkoulouApp.Core.Services;

/// <summary>Individuelle Zugangsdaten, ausschließlich zur einmaligen Übergabe im Arbeitsspeicher.</summary>
public static class ErstzugangService
{
    public static string PasswortErzeugen() => "aA7!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
}
