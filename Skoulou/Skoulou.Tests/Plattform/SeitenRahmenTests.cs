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
    public void Link_auf_die_offene_Seite_mit_anderen_Adressparametern_laedt_neu()
    {
        // R9 (Nutzer 22.11.2026): Vergleich über den Pfad ohne Query und Anker.
        Assert.True(SeitenRahmen.IstNeuLaden("https://skoulou.test/notes?classe=1&periode=2", "https://skoulou.test/notes", ausLink: true));
        Assert.True(SeitenRahmen.IstNeuLaden(Eleves + "?filtre=a-valider", Eleves, ausLink: true));
        Assert.True(SeitenRahmen.IstNeuLaden(Eleves, Eleves + "?classe=5", ausLink: true));
        Assert.True(SeitenRahmen.IstNeuLaden(Eleves + "#liste", Eleves + "/?filtre=actifs", ausLink: true));
        Assert.True(SeitenRahmen.IstNeuLaden("https://skoulou.test/parents/notes?kind=abc", "https://skoulou.test/Parents/Notes", ausLink: true));
    }

    [Fact]
    public void Andere_Adresse_Code_Navigation_und_Sprungmarken_laden_nicht_neu()
    {
        Assert.False(SeitenRahmen.IstNeuLaden(Eleves, "https://skoulou.test/direction/classes", ausLink: true));
        Assert.False(SeitenRahmen.IstNeuLaden(Eleves, Eleves + "/a-valider", ausLink: true));
        Assert.False(SeitenRahmen.IstNeuLaden("https://skoulou.test/admin/facturation", "https://skoulou.test/admin/facturation/parts", ausLink: true));
        // Filter, die eine Seite selbst in die Adresse schreibt (NavigateTo), sind kein Klick auf das Menü – auch mit neuen Parametern.
        Assert.False(SeitenRahmen.IstNeuLaden(Eleves, Eleves, ausLink: false));
        Assert.False(SeitenRahmen.IstNeuLaden("https://skoulou.test/notes", "https://skoulou.test/notes?classe=1&periode=2", ausLink: false));
        Assert.False(SeitenRahmen.IstNeuLaden("https://skoulou.test/notes?classe=1", "https://skoulou.test/notes?classe=1&periode=2", ausLink: false));
        Assert.False(SeitenRahmen.IstNeuLaden(Eleves, Eleves + "#liste", ausLink: true));
        Assert.False(SeitenRahmen.IstNeuLaden(Eleves + "?filtre=1", Eleves + "?filtre=1#liste", ausLink: true));
        Assert.False(SeitenRahmen.IstNeuLaden(null, Eleves, ausLink: true));
    }

    [Fact]
    public void Seiten_schreiben_ihre_Filter_nur_per_Code_in_die_Adresse()
    {
        // R9: Kein Link einer Seite führt nur mit anderen Adressparametern auf ihren eigenen Pfad zurück (das würde die Seite
        // bei jedem Filterklick neu erzeugen); relative Links « ?… » gibt es nicht.
        var seiten = Directory.EnumerateFiles(Path.Combine(Quelle(), "Components"), "*.razor", SearchOption.AllDirectories);
        foreach (var datei in seiten)
        {
            var text = File.ReadAllText(datei);
            Assert.DoesNotMatch("href=\"@?\\(?\\$?\"?\\?", text);
            var routen = System.Text.RegularExpressions.Regex.Matches(text, "@page \"([^\"{]+)\"").Select(m => m.Groups[1].Value.TrimEnd('/')).ToList();
            foreach (var route in routen.Where(r => r.Length > 1))
                Assert.DoesNotContain($"href=\"{route}?", text);
        }
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
