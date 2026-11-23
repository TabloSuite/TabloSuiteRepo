using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Skoulou.Tests.Plattform;

/// <summary>
/// Öffentliche Startseite (23.11.2026): alle Abschnitte blenden beim Scrollen ein wie bei eNOTE; das Kopf-Skript läuft vor
/// blazor.web.js (Einstieg bei langsamem Netz sofort da), Inhalt ohne Skript nie versteckt, Karten heben sich weiter beim Überfahren.
/// </summary>
public class StartseitenAnimationTests
{
    private static string WebApp([CallerFilePath] string datei = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(datei)!, "..", "..", "Skoulou.WebApp"));

    [Fact]
    public void Alle_Abschnitte_der_Startseite_blenden_ein()
    {
        var seite = File.ReadAllText(Path.Combine(WebApp(), "Components", "Pages", "Oeffentlich", "Startseite.razor"));
        var abschnitte = Regex.Matches(seite, "<section class=\"[^\"]*\"[^>]*>.*?</section>", RegexOptions.Singleline);
        Assert.True(abschnitte.Count >= 6, "Abschnitte nicht gefunden.");
        Assert.All(abschnitte, m => Assert.Contains("lp-reveal", m.Value));
        Assert.Contains("<h1 class=\"lp-reveal\"", seite);
        Assert.Contains("<article class=\"lp-funktion lp-reveal\"", seite);
        Assert.Contains("<article class=\"lp-rolle lp-reveal\"", seite);
        Assert.Contains("<article class=\"lp-preis lp-reveal\"", seite);

        // Das Bild im Einstieg behält seine eigene Animation (lp-ein) und wird nicht zusätzlich eingeblendet.
        var vorschau = File.ReadAllText(Path.Combine(WebApp(), "Components", "Pages", "Oeffentlich", "StartseitenVorschau.razor"));
        Assert.Contains("<div class=\"lp-hero-bild\">", vorschau);
        Assert.DoesNotContain("lp-reveal", vorschau);
    }

    [Fact]
    public void Kopf_Skript_vor_Blazor_mit_Rueckfall()
    {
        var app = File.ReadAllText(Path.Combine(WebApp(), "Components", "App.razor"));
        var skript = app.IndexOf("querySelectorAll('.lp-reveal')", StringComparison.Ordinal);
        Assert.True(skript > 0, "Einblend-Skript fehlt.");
        Assert.True(skript < app.IndexOf("</head>", StringComparison.Ordinal), "Das Skript gehört in den Kopf.");
        Assert.True(skript < app.IndexOf("blazor.web.js", StringComparison.Ordinal));
        Assert.Contains("prefers-reduced-motion: reduce", app);
        Assert.Contains("'.lp-hero-bild'", app);              // Einstiegsbild über dasselbe Skript (lp-los, --lp-schon)
        Assert.Contains("--lp-schon", app);
        Assert.Contains("gezeigt[el.lpSchluessel]", app);     // nach dem Ersetzen durch Blazor kein zweites Einblenden
    }

    [Fact]
    public void Css_versteckt_nur_mit_Skript_und_Karten_heben_sich_weiter()
    {
        var css = File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "app.css"));
        Assert.Contains(".lp-js .lp-reveal:not(.sichtbar) { opacity: 0; translate: 0 26px; }", css);
        Assert.DoesNotMatch(new Regex(@"(^|\})\s*\.lp-reveal[^{]*\{[^}]*opacity:\s*0", RegexOptions.Multiline), css);
        Assert.Matches(new Regex(@"\.lp-funktion\.lp-reveal \{ transition: [^}]*box-shadow \.2s, transform \.2s; \}"), css);
        Assert.Contains(".lp-reveal { transition: none !important; }", css);
    }
}
