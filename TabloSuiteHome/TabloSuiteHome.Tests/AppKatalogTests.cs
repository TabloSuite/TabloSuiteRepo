using Microsoft.Extensions.Configuration;
using TabloSuiteHome.WebApp.Services;
using Xunit;

namespace TabloSuiteHome.Tests;

/// <summary>App-Katalog der Firmenseite: echter Katalog gültig, Prüfung erkennt Fehler, Reihenfolge stimmt.</summary>
public class AppKatalogTests
{
    private static TabloSuiteOptionen EchterKatalog()
    {
        var konfig = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "katalog", "appsettings.json"), optional: false)
            .Build();
        var o = new TabloSuiteOptionen();
        konfig.GetSection(TabloSuiteOptionen.Abschnitt).Bind(o);
        return o;
    }

    private static TabloSuiteApp App(string id, AppStatus status = AppStatus.Disponible, int reihe = 0) => new()
    {
        Id = id, Name = id.ToUpperInvariant(), Kategorie = "Test", Beschreibung = "Test", Logo = "img/x.svg",
        Url = $"https://{id}.tablosuite.com", Status = status, Reihenfolge = reihe,
    };

    [Fact]
    public void Echter_Katalog_Ist_Gueltig_Und_Zeigt_ENOTE_Und_SKOULOU()
    {
        var o = EchterKatalog();
        Assert.Empty(o.Pruefe());
        Assert.Equal(["eNOTE", "SKOULOU"], o.SortierteApps.Select(a => a.Name));
        Assert.All(o.SortierteApps, a => Assert.True(a.IstVerfuegbar));
        Assert.Equal("https://enote.tablosuite.com/auth/register", o.SortierteApps[0].RegistrierungUrl);
        Assert.Equal("skoulou.tablosuite.com", o.SortierteApps[1].Domain);
        Assert.Equal("https://skoulou.tablosuite.com/auth/inscription", o.SortierteApps[1].RegistrierungUrl);
        Assert.Equal("https://skoulou.tablosuite.com/tarifs/public", o.SortierteApps[1].TarifQuelle);
        Assert.Equal("https://skoulou.tablosuite.com/#tarifs", o.SortierteApps[1].TarifSeiteUrl);
        Assert.Contains("de façon juste", o.SortierteApps[1].TarifHinweis);
        Assert.Equal(("/ élève / mois", 10), (o.SortierteApps[1].TarifEinheit, o.SortierteApps[1].TarifJahrMonate));   // 19.11.2026: Preis je Monat
        Assert.Equal("contact@tablosuite.com", o.KontaktEmail);
    }

    [Fact]
    public void Bilder_Und_Logos_Des_Katalogs_Existieren()
    {
        var wwwroot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TabloSuiteHome.WebApp", "wwwroot"));
        foreach (var a in EchterKatalog().Apps)
        {
            Assert.True(File.Exists(Path.Combine(wwwroot, a.Logo)), $"Logo fehlt: {a.Logo}");
            if (!string.IsNullOrWhiteSpace(a.Bild)) Assert.True(File.Exists(Path.Combine(wwwroot, a.Bild)), $"Bild fehlt: {a.Bild}");
        }
    }

    [Fact]
    public void Verfuegbare_Zuerst_Dann_Reihenfolge()
    {
        var o = new TabloSuiteOptionen { Apps = [App("bald", AppStatus.Bientot, 0), App("zwei", reihe: 2), App("eins", reihe: 1)] };
        Assert.Equal(["eins", "zwei", "bald"], o.SortierteApps.Select(a => a.Id));
        Assert.Empty(o.Pruefe());
    }

    [Fact]
    public void Pruefung_Erkennt_Fehler()
    {
        var ohneHttps = App("alt"); ohneHttps.Url = "http://alt.tablosuite.com";
        var bald = App("neu", AppStatus.Bientot); bald.Url = string.Empty;          // « bientôt » braucht noch keine Adresse
        var o = new TabloSuiteOptionen { Apps = [App("enote"), App("ENOTE"), ohneHttps, bald, new TabloSuiteApp { Id = "Mit Leerzeichen" }] };
        var fehler = o.Pruefe();
        Assert.Contains(fehler, f => f.Contains("en double"));
        Assert.Contains(fehler, f => f.Contains("« ALT »") && f.Contains("https"));
        Assert.Contains(fehler, f => f.Contains("Id invalide"));
        Assert.DoesNotContain(fehler, f => f.Contains("« NEU »"));
        Assert.Contains("Aucune application", new TabloSuiteOptionen().Pruefe().Single());
    }
}
