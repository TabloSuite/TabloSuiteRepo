using ENote.Core.DataModels.Salons;

namespace ENote.WebApp.Services;

/// <summary>Ziel des QR-Codes auf dem Bon: eigener Link des Ladens, sonst die Firmenseite TabloSuite.</summary>
public static class BonQr
{
    /// <summary>Firmenseite ohne „www“: kürzerer Text = gröbere QR-Punkte, besser lesbar auf Thermopapier.</summary>
    public const string Standard = "https://tablosuite.com";

    /// <summary>Öffentliche Adresse der eNOTE-App (Ersatz für Links in Mails, nicht für den Bon).</summary>
    public const string AppSeite = "https://enote.tablosuite.com";

    public static string Ziel(Salon salon, IConfiguration konfiguration)
        => !string.IsNullOrWhiteSpace(salon.BonQrLink)
            ? salon.BonQrLink.Trim()
            : konfiguration["App:BonQrStandard"] is { Length: > 0 } eigener ? eigener : Standard;
}
