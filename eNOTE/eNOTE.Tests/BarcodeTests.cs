using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Katalog;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

public class BarcodeTests
{
    [Theory]
    [InlineData("4006381333931", BarcodeRegeln.Art.Ean13, false)]   // gültige Hersteller-EAN
    [InlineData("4006381333932", BarcodeRegeln.Art.Ean13, true)]    // Prüfziffer falsch → Warnung
    [InlineData("96385074", BarcodeRegeln.Art.Ean8, false)]
    [InlineData("036000291452", BarcodeRegeln.Art.UpcA, false)]
    [InlineData(" 400-638 1333931 ", BarcodeRegeln.Art.Ean13, false)] // Leerzeichen/Bindestriche vom Abtippen
    [InlineData("TISSU-A3", BarcodeRegeln.Art.Frei, false)]
    public void Erkennt_Formate_Und_Prueft_Pruefziffer(string eingabe, BarcodeRegeln.Art art, bool warnung)
    {
        var p = BarcodeRegeln.Pruefe(eingabe);
        Assert.Equal((art, warnung, false), (p.Art, p.Warnung, p.Fehler));
    }

    [Theory]
    [InlineData("12")]
    [InlineData("ÉTÉ2026")]
    [InlineData("CAFÉ-NOIR")]
    public void Ungueltige_Eingaben_Sind_Fehler(string eingabe)
        => Assert.True(BarcodeRegeln.Pruefe(eingabe).Fehler);

    [Theory]
    [InlineData("https://id.gs1.org/01/09506000134352/10/ABC123?17=271231")] // GS1 Digital Link
    [InlineData("http://example.tg/p?id=42&lot=7")]
    [InlineData("A&B#1234")]
    public void Original_QR_Codes_der_Hersteller_werden_angenommen(string inhalt)
    {
        var p = BarcodeRegeln.Pruefe(inhalt);
        Assert.False(p.Fehler);
        Assert.Equal(BarcodeRegeln.Art.Frei, p.Art);
    }

    [Fact]
    public void Lange_QR_Inhalte_bis_500_Zeichen()
    {
        var lang = "https://beispiel.tg/" + new string('a', BarcodeRegeln.MaxLaenge - 20);
        Assert.False(BarcodeRegeln.Pruefe(lang).Fehler);
        Assert.Contains("QR", BarcodeRegeln.Pruefe(lang).Hinweis);
        Assert.True(BarcodeRegeln.Pruefe(lang + "b").Fehler);
    }

    [Theory]
    [InlineData("ABC123", "abc-123")]                              // Bindestrich und Kleinschreibung beim Scannen
    [InlineData("4006381333931", " 4006381333931 ")]
    [InlineData("HTTPS://ID.GS1.ORG/01/09506000134352", "https://id.gs1.org/01/09506000134352")]
    public void Kasse_findet_gespeicherten_Code_auch_bei_anderer_Schreibweise(string gespeichert, string gescannt)
        => Assert.True(BarcodeRegeln.Gleich(gespeichert, gescannt));

    [Fact]
    public void Leere_Codes_sind_nie_gleich()
        => Assert.False(BarcodeRegeln.Gleich(null, null));

    [Fact]
    public void Etikett_druckt_lange_Inhalte_als_QR_Code()
    {
        var kurz = EtikettPdfService.ErzeugeCode128("TISSU-A3");
        var lang = EtikettPdfService.ErzeugeCode128("HTTPS://ID.GS1.ORG/01/09506000134352/10/ABC123?17=271231");
        Assert.NotNull(kurz);
        Assert.NotNull(lang);
        // Code 128 ist breit (300×60), der QR-Code quadratisch (120×120).
        Assert.Contains("viewBox=\"0 0 300 60\"", kurz);
        Assert.Contains("viewBox=\"0 0 120 120\"", lang);
    }

    [Fact]
    public void Interne_Codes_Sind_Gueltige_Ean13_Mit_Praefix_2()
    {
        var zufall = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var code = BarcodeRegeln.ErzeugeIntern(zufall);
            Assert.Equal(13, code.Length);
            Assert.StartsWith("2", code);
            Assert.True(BarcodeRegeln.HatGueltigePruefziffer(code));
            Assert.Equal(BarcodeRegeln.Art.Intern, BarcodeRegeln.Pruefe(code).Art);
        }
    }

    [Fact]
    public async Task Leer_Wird_Automatisch_Vergeben_Dubletten_Abgelehnt_Und_Code_Bleibt_Beim_Bearbeiten()
    {
        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Name = "S", IstAktiv = true, IstFreigegeben = true };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            await db.SaveChangesAsync();
        }
        var service = new KatalogService(factory, null!, null!);

        // Leer → interner EAN-13
        var a = await service.SpeichereLeistungAsync(new Leistung { Id = Guid.Empty, SalonId = salon.Id, Name = "Huile", Preis = 3000 });
        Assert.Equal(BarcodeRegeln.Art.Intern, BarcodeRegeln.Pruefe(a.StrichCode).Art);

        // Hersteller-Code wird normalisiert übernommen
        var b = await service.SpeichereLeistungAsync(new Leistung { Id = Guid.Empty, SalonId = salon.Id, Name = "Shampoing", Preis = 4000, StrichCode = "400 6381 333931" });
        Assert.Equal("4006381333931", b.StrichCode);

        // Dublette im selben Laden → Fehler mit Namen des anderen Artikels
        var fehler = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SpeichereLeistungAsync(new Leistung { Id = Guid.Empty, SalonId = salon.Id, Name = "Autre", Preis = 1, StrichCode = "4006381333931" }));
        Assert.Contains("Shampoing", fehler.Message);
        var hinweis = await service.PruefeBarcodeAsync(salon.Id, "4006381333931", Guid.Empty);
        Assert.True(hinweis.Fehler);
        Assert.False((await service.PruefeBarcodeAsync(salon.Id, "4006381333931", b.Id)).Fehler); // eigener Code ist ok

        // Bearbeiten mit unverändertem Code behält ihn; geleert → neuer interner Code
        var bearbeitet = await service.SpeichereLeistungAsync(new Leistung { Id = b.Id, SalonId = salon.Id, Name = "Shampoing bio", Preis = 4500, StrichCode = b.StrichCode });
        Assert.Equal("4006381333931", bearbeitet.StrichCode);
        var geleert = await service.SpeichereLeistungAsync(new Leistung { Id = b.Id, SalonId = salon.Id, Name = "Shampoing bio", Preis = 4500, StrichCode = "  " });
        Assert.Equal(BarcodeRegeln.Art.Intern, BarcodeRegeln.Pruefe(geleert.StrichCode).Art);

        await using var pruefung = await factory.CreateDbContextAsync();
        Assert.Equal(2, await pruefung.Leistungen.Select(l => l.StrichCode).Distinct().CountAsync());
    }
}
