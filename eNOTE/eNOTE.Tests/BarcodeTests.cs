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
    [InlineData("A&B#1234")]
    public void Ungueltige_Eingaben_Sind_Fehler(string eingabe)
        => Assert.True(BarcodeRegeln.Pruefe(eingabe).Fehler);

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
