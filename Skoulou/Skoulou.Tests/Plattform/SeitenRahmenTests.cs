using Skoulou.WebApp.Components.Shared;

namespace Skoulou.Tests.Plattform;

/// <summary>
/// Nutzerentscheidung 22.11.2026 (Klicktest Befund 8): Ein Klick auf den Menüeintrag der gerade offenen Seite lädt sie neu
/// (Élèves: Validierungshinweis, Notes: Raster nach Öffnen einer Rectification, Mes parts nach einer Auszahlung).
/// </summary>
public class SeitenRahmenTests
{
    private const string Eleves = "https://skoulou.test/direction/eleves";

    [Fact]
    public void Link_auf_die_offene_Adresse_laedt_neu()
    {
        Assert.True(SeitenRahmen.IstNeuLaden(Eleves, Eleves, ausLink: true));
        Assert.True(SeitenRahmen.IstNeuLaden(Eleves + "/", Eleves, ausLink: true));
        Assert.True(SeitenRahmen.IstNeuLaden(Eleves + "#liste", Eleves, ausLink: true));
        Assert.True(SeitenRahmen.IstNeuLaden("https://skoulou.test/notes?classe=1&periode=2", "https://skoulou.test/notes?classe=1&periode=2", ausLink: true));
    }

    [Fact]
    public void Andere_Adresse_Code_Navigation_und_Sprungmarken_laden_nicht_neu()
    {
        Assert.False(SeitenRahmen.IstNeuLaden(Eleves, "https://skoulou.test/direction/classes", ausLink: true));
        Assert.False(SeitenRahmen.IstNeuLaden("https://skoulou.test/notes?classe=1", "https://skoulou.test/notes", ausLink: true));
        // Filter, die eine Seite selbst in die Adresse schreibt (NavigateTo), sind kein Klick auf das Menü.
        Assert.False(SeitenRahmen.IstNeuLaden(Eleves, Eleves, ausLink: false));
        Assert.False(SeitenRahmen.IstNeuLaden(Eleves, Eleves + "#liste", ausLink: true));
        Assert.False(SeitenRahmen.IstNeuLaden(null, Eleves, ausLink: true));
    }

    [Fact]
    public void Hauptlayout_umschliesst_die_Seite_mit_dem_Rahmen()
    {
        var layout = File.ReadAllText(Path.Combine(Quelle(), "Components", "Layout", "MainLayout.razor"));
        Assert.Contains("<SeitenRahmen @key=\"seitenSchluessel\">@Body</SeitenRahmen>", layout);
        Assert.Contains("Nav.LocationChanged -= Navigiert", layout);
    }

    private static string Quelle()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "Skoulou.WebApp"))) d = d.Parent;
        return Path.Combine(d!.FullName, "Skoulou.WebApp");
    }
}
