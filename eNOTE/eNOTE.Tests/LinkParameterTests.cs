using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Fehlersuche 21.11.2026: « /caisse?kunde=kaputt » ließ die Kasse in die Fehlergrenze laufen, « /auth/reinitialiser?compte=kaputt »
/// lieferte HTTP 500. Die Seiten binden die Parameter jetzt als Text und lesen sie über <see cref="LinkParameter"/>.
/// </summary>
public class LinkParameterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kaputt")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("9DEEFA7A-9809-4D0A-95B9")]
    public void Unlesbare_Werte_Gelten_Als_Kein_Wert(string? text) => Assert.Null(LinkParameter.AlsGuid(text));

    [Fact]
    public void Gueltige_Guid_Wird_Gelesen()
    {
        var g = Guid.NewGuid();
        Assert.Equal(g, LinkParameter.AlsGuid(g.ToString()));
        Assert.Equal(g, LinkParameter.AlsGuid(" " + g.ToString("N").ToUpperInvariant() + " "));
    }

    [Fact]
    public void Seiten_Binden_Die_Link_Parameter_Als_Text()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "eNOTE.WebApp"))) dir = dir.Parent;
        var seiten = Path.Combine(dir!.FullName, "eNOTE.WebApp", "Components", "Pages");
        Assert.Contains("public string? KundeAusLinkText", File.ReadAllText(Path.Combine(seiten, "Caisse.razor.cs")));
        Assert.Contains("public string? CompteText", File.ReadAllText(Path.Combine(seiten, "PasswortZuruecksetzen.razor")));
        // Keine Seite bindet mehr einen Guid direkt aus der Query (wirft bei kaputten Links)
        foreach (var datei in Directory.GetFiles(seiten, "*.razor*"))
            Assert.DoesNotMatch(@"SupplyParameterFromQuery[^\]]*\]\s*public\s+Guid", File.ReadAllText(datei));
    }
}
