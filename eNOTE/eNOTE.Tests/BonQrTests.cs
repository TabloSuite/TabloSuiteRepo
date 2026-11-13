using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ENote.Tests;

public class BonQrTests
{
    private static IConfiguration Konfiguration(string? standard = null)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:BonQrStandard"] = standard
        }).Build();

    [Fact]
    public void Ohne_eigenen_Link_fuehrt_der_QR_Code_zur_Firmenseite_ohne_www()
    {
        var ziel = BonQr.Ziel(new Salon { Name = "Salon Test" }, Konfiguration());

        Assert.Equal("https://tablosuite.com", ziel);
    }

    [Fact]
    public void Eigener_Link_des_Ladens_hat_Vorrang()
    {
        var ziel = BonQr.Ziel(new Salon { Name = "Salon Test", BonQrLink = "  https://wa.me/22890000000 " }, Konfiguration());

        Assert.Equal("https://wa.me/22890000000", ziel);
    }

    [Fact]
    public void Standard_kann_per_Konfiguration_ueberschrieben_werden()
    {
        var ziel = BonQr.Ziel(new Salon { Name = "Salon Test" }, Konfiguration("https://beispiel.tg"));

        Assert.Equal("https://beispiel.tg", ziel);
    }

    [Fact]
    public void Mail_Links_bleiben_bei_der_eNOTE_App()
    {
        Assert.Equal("https://enote.tablosuite.com", BonQr.AppSeite);
    }
}
