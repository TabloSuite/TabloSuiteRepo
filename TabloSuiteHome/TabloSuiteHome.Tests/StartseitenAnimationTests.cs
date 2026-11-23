using System.Text.RegularExpressions;
using Xunit;

namespace TabloSuiteHome.Tests;

/// <summary>
/// Einblenden beim Scrollen (23.11.2026) wie auf den Startseiten von eNOTE und SKOULOU: IntersectionObserver statt
/// « animation-timeline » (das Safari/iPhone und Firefox nicht kennen), alle Abschnitte markiert, Inhalt nie dauerhaft versteckt.
/// </summary>
public class StartseitenAnimationTests
{
    private static string WebApp(params string[] teile)
        => Path.GetFullPath(Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "TabloSuiteHome.WebApp", .. teile]));

    private static string Lies(params string[] teile) => File.ReadAllText(WebApp(teile));

    [Fact]
    public void Css_braucht_kein_animation_timeline_und_versteckt_nur_mit_Skript()
    {
        var css = Lies("wwwroot", "app.css");
        Assert.DoesNotMatch(new Regex(@"animation-(timeline|range)\s*:"), css);   // auch nicht in @supports (…)
        Assert.Contains(".lp-js .lp-reveal:not(.sichtbar) { opacity: 0; translate: 0 26px; }", css);
        // Versteckt wird nur unter lp-js (Skript lief) – ohne JavaScript bleibt alles sichtbar.
        Assert.DoesNotMatch(new Regex(@"(^|\})\s*\.lp-reveal[^{]*\{[^}]*opacity:\s*0", RegexOptions.Multiline), css);
    }

    [Fact]
    public void Skript_im_Kopf_vor_dem_Stylesheet_mit_Rueckfall()
    {
        var app = Lies("Components", "App.razor");
        var skript = app.IndexOf("IntersectionObserver", StringComparison.Ordinal);
        Assert.True(skript > 0, "Kopf-Skript mit IntersectionObserver fehlt.");
        Assert.True(skript < app.IndexOf("</head>", StringComparison.Ordinal), "Das Skript gehört in den Kopf.");
        Assert.True(skript < app.IndexOf("app.css", StringComparison.Ordinal), "Das Skript läuft vor dem Stylesheet.");
        Assert.Contains("prefers-reduced-motion: reduce", app);
        Assert.Contains("classList.add('lp-js')", app);
        Assert.Contains("classList.add('sichtbar')", app);
    }

    [Fact]
    public void Alle_Abschnitte_der_Startseite_blenden_ein()
    {
        var home = Lies("Components", "Pages", "Home.razor");
        foreach (var abschnitt in new[] { "applications", "pourquoi", "contact" })
        {
            var start = home.IndexOf($"<section id=\"{abschnitt}\"", StringComparison.Ordinal);
            Assert.True(start >= 0, $"Abschnitt fehlt: {abschnitt}");
            var ende = home.IndexOf("</section>", start, StringComparison.Ordinal);
            Assert.Contains("lp-reveal", home[start..ende]);
        }
        Assert.Contains("<article class=\"ts-wert lp-reveal\"", home);

        var karte = Lies("Components", "Shared", "AppKarte.razor");
        Assert.Contains("class=\"ts-app lp-reveal", karte);
        Assert.Contains("class=\"ts-app-text lp-reveal", karte);
        // Das Bild gleitet mit der Karte herein (kein eigener Auslöser, sonst stünde die Karte am PC kurz leer da).
        Assert.Contains(".lp-js .ts-app.lp-reveal:not(.sichtbar) .ts-app-bild { opacity: 0;", Lies("wwwroot", "app.css"));
    }
}
