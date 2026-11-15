using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Skoulou.Tests.Plattform;

/// <summary>
/// Wächter für die Oberfläche (Klick-Simulation S4): kein Zeichenverlust bei langsamer Verbindung, Ladekreis der
/// klassischen Formulare.
/// </summary>
public partial class OberflaechenTests
{
    private static string WebApp([CallerFilePath] string datei = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(datei)!, "..", "..", "Skoulou.WebApp"));

    private static IEnumerable<(string Datei, string Text)> Komponenten()
        => Directory.EnumerateFiles(Path.Combine(WebApp(), "Components"), "*.razor", SearchOption.AllDirectories)
            .Select(d => (Path.GetRelativePath(WebApp(), d), File.ReadAllText(d)));

    [GeneratedRegex(@"<(input|textarea)\b[^>]*>", RegexOptions.Singleline)]
    private static partial Regex Eingabefeld();

    [GeneratedRegex(@"<form\b[^>]*>", RegexOptions.Singleline)]
    private static partial Regex Formular();

    /// <summary>
    /// « value="@x" @oninput » überschreibt bei langsamer Verbindung das Feld mit einem alten Stand (in S4 wurde aus
    /// « Clic2026sim-test » « Clst »). Erlaubt ist nur @bind bzw. @bind:get/@bind:set – dann gleicht Blazor mit dem Browser ab.
    /// </summary>
    [Fact]
    public void Kein_Eingabefeld_mit_value_und_oninput()
    {
        var verstoesse = Komponenten()
            .SelectMany(k => Eingabefeld().Matches(k.Text).Select(m => (k.Datei, Tag: m.Value)))
            .Where(x => x.Tag.Contains("@oninput", StringComparison.Ordinal) && x.Tag.Contains("value=\"@", StringComparison.Ordinal))
            .Select(x => x.Datei)
            .Distinct()
            .ToList();
        Assert.True(verstoesse.Count == 0, "value + @oninput (Zeichenverlust): " + string.Join(", ", verstoesse));
    }

    /// <summary>Jedes klassische Formular zeigt den Ladekreis sofort im Browser (data-ladeanzeige, skoulou.js).</summary>
    [Fact]
    public void Jedes_klassische_Formular_hat_die_Ladeanzeige()
    {
        var formulare = Komponenten()
            .SelectMany(k => Formular().Matches(k.Text).Select(m => (k.Datei, Tag: m.Value)))
            .Where(x => x.Tag.Contains("method=\"post\"", StringComparison.Ordinal))
            .ToList();
        Assert.True(formulare.Count >= 9, "klassische Formulare nicht gefunden");
        var ohne = formulare.Where(x => !x.Tag.Contains("data-ladeanzeige=\"", StringComparison.Ordinal)).Select(x => x.Datei).ToList();
        Assert.True(ohne.Count == 0, "Formular ohne data-ladeanzeige: " + string.Join(", ", ohne));
    }

    /// <summary>
    /// Formulare mit Prüfung in Blazor (« skFormularSenden »): Absende-Knopf vom Typ submit (Ladekreis sofort, auch bevor
    /// die Verbindung steht), bei einem Fehler gibt « skFormularFrei » den Knopf wieder frei.
    /// </summary>
    [Fact]
    public void Formulare_mit_Pruefung_geben_den_Knopf_bei_Fehlern_frei()
    {
        var wurzel = Path.Combine(WebApp(), "Components");
        var dateien = Directory.EnumerateFiles(wurzel, "*.razor*", SearchOption.AllDirectories)
            .Where(d => File.ReadAllText(d).Contains("skFormularSenden", StringComparison.Ordinal)).ToList();
        Assert.True(dateien.Count >= 5);
        foreach (var datei in dateien)
        {
            var text = File.ReadAllText(datei);
            Assert.True(text.Contains("skFormularFrei", StringComparison.Ordinal), Path.GetFileName(datei) + " : skFormularFrei fehlt");
            var razor = datei.EndsWith(".cs", StringComparison.Ordinal) ? File.ReadAllText(datei[..^3]) : text;
            Assert.True(razor.Contains("type=\"submit\"", StringComparison.Ordinal), Path.GetFileName(datei) + " : Knopf vom Typ submit fehlt");
            // Der Knopf darf die Prüfung nicht zusätzlich selbst auslösen (sonst wird doppelt gesendet).
            Assert.DoesNotContain("OnClick=\"" + Regex.Match(razor, "@onsubmit=\"(\\w+)\"").Groups[1].Value + "\"", razor);
        }

        var js = File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "js", "skoulou.js"));
        Assert.Contains("window.skFormularSenden", js);
        Assert.Contains("window.skFormularFrei", js);
        Assert.Contains("'pageshow'", js);
        Assert.Contains("data-sk-laedt", js);
        var css = File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "app.css"));
        Assert.Contains(".sk-btn[data-sk-laedt]::before", css);
        Assert.DoesNotContain("color-mix", css);
    }
}
