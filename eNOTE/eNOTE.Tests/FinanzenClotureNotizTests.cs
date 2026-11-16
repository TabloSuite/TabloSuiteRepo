using Xunit;

namespace ENote.Tests;

/// <summary>
/// Finances › « Clôtures de caisse » (16.11.2026): die Notiz, die beim Clôturer zu einem Écart geschrieben wird
/// (<c>KassenSitzung.AbschlussNotiz</c>), wurde gespeichert, aber nirgends angezeigt – der Besitzer sah sie nie.
/// </summary>
public class FinanzenClotureNotizTests
{
    private static string WebApp()
    {
        var ordner = new DirectoryInfo(AppContext.BaseDirectory);
        while (ordner is not null && !Directory.Exists(Path.Combine(ordner.FullName, "eNOTE", "eNOTE.WebApp"))) ordner = ordner.Parent;
        Assert.NotNull(ordner);
        return Path.Combine(ordner!.FullName, "eNOTE", "eNOTE.WebApp");
    }

    [Fact]
    public void Cloture_zeigt_die_Notiz_des_Abschlusses()
    {
        var seite = File.ReadAllText(Path.Combine(WebApp(), "Components", "Pages", "Finanzen.razor"));
        Assert.Contains("@if (!string.IsNullOrWhiteSpace(a.AbschlussNotiz))", seite);
        Assert.Contains("<small class=\"fi-abschluss-notiz\">« @a.AbschlussNotiz »</small>", seite);
        var css = File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "app.css"));
        Assert.Contains(".fi-abschluss-notiz {", css);
    }
}
