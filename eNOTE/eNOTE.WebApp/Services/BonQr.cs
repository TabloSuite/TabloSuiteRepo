using ENote.Core.DataModels.Salons;

namespace ENote.WebApp.Services;

/// <summary>Ziel des QR-Codes auf dem Bon: eigener Link des Ladens, sonst die öffentliche eNOTE-Seite.</summary>
public static class BonQr
{
    public const string Standard = "https://enote.tablosuite.com";

    public static string Ziel(Salon salon, IConfiguration konfiguration)
        => !string.IsNullOrWhiteSpace(salon.BonQrLink)
            ? salon.BonQrLink.Trim()
            : konfiguration["App:BonQrStandard"] is { Length: > 0 } eigener ? eigener : Standard;
}
