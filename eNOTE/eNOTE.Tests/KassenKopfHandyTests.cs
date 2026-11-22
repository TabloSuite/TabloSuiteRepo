using System.Text.RegularExpressions;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Normaltest 21.11.2026 (Handy 390 px, Besitzerin kassiert selbst): Die sieben Aktionen des Kassenkopfs (Présents …
/// Clôturer) brauchen fast die ganze Breite; Ladenname, Person und der Knopf « Changer » liefen darunter und « Changer »
/// war in der Mitte von « Clients présents » überdeckt – der Personalwechsel ging nur mit einem Tipp auf den Rand.
/// Am Handy bricht der Kopf jetzt um: Info oben, Aktionen darunter.
/// </summary>
public class KassenKopfHandyTests
{
    private static string WebApp()
    {
        var ordner = new DirectoryInfo(AppContext.BaseDirectory);
        while (ordner is not null && !Directory.Exists(Path.Combine(ordner.FullName, "eNOTE", "eNOTE.WebApp"))) ordner = ordner.Parent;
        Assert.NotNull(ordner);
        return Path.Combine(ordner!.FullName, "eNOTE", "eNOTE.WebApp");
    }

    [Fact]
    public void Kassenkopf_bricht_am_Handy_um_damit_Changer_frei_bleibt()
    {
        var css = File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "app.css"));
        var handy = Regex.Matches(css, @"@media \(max-width: 640px\) \{(?<inhalt>[\s\S]*?)\n\}")
            .Select(m => m.Groups["inhalt"].Value)
            .FirstOrDefault(b => b.Contains(".kx-kopf {"));
        Assert.NotNull(handy);
        Assert.Contains(".kx-kopf { flex-wrap: wrap; }", handy);
        Assert.Contains(".kx-kopf-info { flex: 1 1 100%; }", handy);
        Assert.Contains(".kx-kopf-aktionen { flex: 1 1 100%;", handy);
        // Der Knopf selbst bleibt im Info-Bereich (keine zweite Variante nur fürs Handy)
        var kasse = File.ReadAllText(Path.Combine(WebApp(), "Components", "Pages", "Caisse.razor"));
        Assert.Contains("class=\"kx-kassierer-wechsel\"", kasse);
    }
}
