using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

public class BonPdfTests
{
    [Fact]
    public void ErzeugeBon_Mit_Branding_Und_Rabatt()
    {
        var salon = ErstelleBeispielSalon();
        var verkauf = ErstelleBeispielVerkauf();

        var pdf = BonPdfService.ErzeugeBon(verkauf, salon, "Awa Mensah");

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 500);

        BeispielAblegen(pdf, "enote-beispiel-bon.pdf");
    }

    [Fact]
    public void ErzeugeBon_Mit_Logo_Und_QrCode()
    {
        var salon = ErstelleBeispielSalon();
        var verkauf = ErstelleBeispielVerkauf();
        var logo = ErzeugeTestLogo();

        var pdf = BonPdfService.ErzeugeBon(verkauf, salon, "Awa Mensah", logo, "https://wa.me/22890000000");

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 2000);

        BeispielAblegen(pdf, "enote-beispiel-bon-mit-logo.pdf");
    }

    /// <summary>
    /// Beispiel-PDF zum Ansehen nur auf Wunsch ablegen (Umgebungsvariable <c>ENOTE_PDF_BILDER</c> = Ordner). Vorher lag bei jedem
    /// Testlauf ein neues PDF im TEMP-Ordner (am 22.11.2026 waren es 749).
    /// </summary>
    private static void BeispielAblegen(byte[] pdf, string datei)
    {
        if (Environment.GetEnvironmentVariable("ENOTE_PDF_BILDER") is not { Length: > 0 } ordner) return;
        Directory.CreateDirectory(ordner);
        File.WriteAllBytes(Path.Combine(ordner, datei), pdf);
    }

    private static Salon ErstelleBeispielSalon() => new()
    {
        Name = "Salon Élégance",
        Standort = "Rue de la Paix, Lomé",
        Slogan = "In God we trust"
    };

    private static Verkauf ErstelleBeispielVerkauf() => new()
    {
        ZeitUtc = new DateTime(2026, 11, 3, 14, 30, 0, DateTimeKind.Utc),
        ZahlungsArt = ZahlungsArt.Bar,
        Zwischensumme = 5500,
        RabattSumme = 500,
        GesamtSumme = 5000,
        Gegeben = 10000,
        Rueckgeld = 5000,
        Positionen = new List<VerkaufsPosition>
        {
            new() { LeistungName = "Coupe Homme", EinzelPreis = 3000, Menge = 1, ZeilenSumme = 3000 },
            new() { LeistungName = "Barbe", EinzelPreis = 1500, Menge = 1, RabattBetrag = 500, ZeilenSumme = 1000 },
            new() { LeistungName = "Boisson gazeuse", EinzelPreis = 500, Menge = 2, ZeilenSumme = 1000 }
        }
    };

    /// <summary>Erzeugt ein kleines gültiges PNG (violetter Block mit „eN“) als Test-Logo.</summary>
    private static byte[] ErzeugeTestLogo()
    {
        // 1×1 PNG, violett (RGB 99,102,241). Gültiges Minimal-PNG.
        // QuestPDF skaliert es auf den Bon; für die Vorschau nutzen wir eine größere Fläche via QuestPDF-intern.
        var png = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
            0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE, 0x00, 0x00, 0x00,
            0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xE4, 0x64, 0x61, 0x60,
            0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0xC5, 0x6A, 0x2A, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
        };
        return png;
    }
}

