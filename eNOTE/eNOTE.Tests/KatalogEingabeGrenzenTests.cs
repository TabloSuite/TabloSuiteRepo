using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Fehlersuche 21.11.2026: ein 17-stelliger Preis bzw. Festbetrag einer Remise sprengte decimal(18,2) (Datenbankfehler,
/// Fehlergrenze « Un problème est survenu »), ein Artikelname über 200 Zeichen ließ « Déclarer une perte » und « Retrait »
/// scheitern (Kopie in nvarchar(200)), eine Kategorie mit 460 Zeichen den eindeutigen Index (nvarchar(450)).
/// </summary>
public class KatalogEingabeGrenzenTests
{
    private static async Task<(KatalogService Dienst, Guid SalonId)> WeltAsync()
    {
        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Id = Guid.NewGuid(), Name = "Grenzen " + Guid.NewGuid().ToString("N")[..6] };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            await db.SaveChangesAsync();
        }
        return (new KatalogService(factory, null!, null!), salon.Id);
    }

    [Theory]
    [InlineData("99999999999999999")]
    [InlineData("1000000000")]
    public async Task Zu_Hoher_Preis_Wird_Mit_Meldung_Abgelehnt(string preis)
    {
        var (dienst, salonId) = await WeltAsync();
        var fehler = await Assert.ThrowsAsync<InvalidOperationException>(() => dienst.SpeichereLeistungAsync(
            new Leistung { Id = Guid.Empty, SalonId = salonId, Name = "Énorme", Preis = decimal.Parse(preis) }));
        Assert.Contains("999.999.999", fehler.Message);
    }

    [Fact]
    public async Task Hoechstpreis_Und_Normale_Werte_Gehen_Durch()
    {
        var (dienst, salonId) = await WeltAsync();
        var l = await dienst.SpeichereLeistungAsync(new Leistung { Id = Guid.Empty, SalonId = salonId, Name = new string('a', KatalogService.LeistungNameMax), Preis = KatalogService.BetragMax });
        Assert.Equal(KatalogService.BetragMax, l.Preis);
    }

    [Fact]
    public async Task Zu_Hoher_Einkaufspreis_Oder_Stock_Wird_Abgelehnt()
    {
        var (dienst, salonId) = await WeltAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => dienst.SpeichereLeistungAsync(
            new Leistung { Id = Guid.Empty, SalonId = salonId, Name = "Achat", Typ = LeistungsTyp.Produit, Preis = 1000, Kaufpreis = 99999999999999999m }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => dienst.SpeichereLeistungAsync(
            new Leistung { Id = Guid.Empty, SalonId = salonId, Name = "Stock", Typ = LeistungsTyp.Produit, Preis = 1000, HatStueckzahl = true, Stueckzahl = 99999999999999999m }));
    }

    [Fact]
    public async Task Name_Ueber_200_Zeichen_Wird_Abgelehnt()
    {
        var (dienst, salonId) = await WeltAsync();
        var fehler = await Assert.ThrowsAsync<InvalidOperationException>(() => dienst.SpeichereLeistungAsync(
            new Leistung { Id = Guid.Empty, SalonId = salonId, Name = new string('x', KatalogService.LeistungNameMax + 1), Preis = 1000 }));
        Assert.Contains("200 caractères", fehler.Message);
    }

    [Fact]
    public async Task Kategorie_Mit_460_Zeichen_Wird_Ueberall_Abgelehnt()
    {
        var (dienst, salonId) = await WeltAsync();
        var lang = new string('K', 460);
        await Assert.ThrowsAsync<ArgumentException>(() => dienst.SpeichereKategorieAsync(salonId, lang));
        var haupt = await dienst.SpeichereKategorieAsync(salonId, "Coiffure");
        var (ok, fehler, _) = await dienst.SpeichereUnterkategorieAsync(salonId, haupt.Id, lang);
        Assert.False(ok);
        Assert.Contains("100 caractères", fehler);
        var (ok2, fehler2) = await dienst.BenenneKategorieUmAsync(haupt.Id, salonId, lang);
        Assert.False(ok2);
        Assert.Contains("100 caractères", fehler2);
    }

    [Fact]
    public void Remise_Festbetrag_Ueber_Hoechstbetrag_Wird_Abgelehnt()
    {
        Assert.Contains("999.999.999", KatalogService.PruefeRabatt(new Rabatt { Name = "Énorme", Art = RabattArt.Festbetrag, Wert = 99999999999999999m }));
        Assert.Null(KatalogService.PruefeRabatt(new Rabatt { Name = "Ok", Art = RabattArt.Festbetrag, Wert = 500 }));
    }
}
