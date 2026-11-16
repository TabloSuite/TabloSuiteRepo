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
    /// Verbindung unterbrochen: eigenes französisches Fenster (sonst zeigt Blazor « Rejoining the server… » auf Englisch).
    /// Blazor findet es über die Kennung, zeigt es über Klassen; die Knöpfe bedient skoulou.js.
    /// </summary>
    [Fact]
    public void Fenster_Verbindung_unterbrochen_ist_franzoesisch_und_eingebunden()
    {
        var fenster = File.ReadAllText(Path.Combine(WebApp(), "Components", "Layout", "VerbindungsFenster.razor"));
        Assert.Contains("id=\"components-reconnect-modal\"", fenster);
        Assert.Contains("id=\"components-seconds-to-next-attempt\"", fenster);
        Assert.Contains("Connexion interrompue", fenster);
        Assert.Contains("Réessayer", fenster);
        Assert.Contains("Recharger la page", fenster);
        Assert.DoesNotMatch(new Regex(@"\b(Rejoin|Retry|Resume|reload the page)\b", RegexOptions.IgnoreCase), fenster);

        // Statisch in App.razor: das Fenster muss auch stehen, wenn die interaktive Seite gerade nicht antwortet.
        var app = File.ReadAllText(Path.Combine(WebApp(), "Components", "App.razor"));
        Assert.Contains("<VerbindungsFenster />", app);

        var css = File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "app.css"));
        foreach (var zustand in new[] { "show", "retrying", "failed", "paused", "resume-failed" })
            Assert.Contains(".components-reconnect-" + zustand, css);

        var js = File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "js", "skoulou.js"));
        Assert.Contains("components-reconnect-state-changed", js);
        Assert.Contains("Blazor.reconnect()", js);
    }

    /// <summary>
    /// Handy (390 px): Leisten, die unten kleben, stehen über der unteren Navigation (« Enregistrer l'appel » war verdeckt);
    /// Kalender und Aufklapplisten bleiben im Bildschirm; Kartenkopf, Formulare und Annoncen brechen um.
    /// </summary>
    [Fact]
    public void Handy_nichts_verdeckt_und_nichts_ragt_hinaus()
    {
        string Css(string datei) => File.ReadAllText(Path.Combine(WebApp(), "wwwroot", datei));
        var mobil = "@media (max-width: 640px) { ";
        Assert.Contains(mobil + ".org-appell-fuss { bottom: calc(4.35rem + env(safe-area-inset-bottom));", Css("css/organisation.css"));
        Assert.Contains(mobil + ".nt-statusleiste { bottom: calc(4.35rem + env(safe-area-inset-bottom));", Css("css/noten.css"));
        Assert.Contains(mobil + ".sk-editor-fuss { bottom: calc(4.9rem + env(safe-area-inset-bottom));", Css("css/paedagogik.css"));

        var app = Css("app.css");
        Assert.Contains(".sk-auswahl-popup, .sk-datum-popup { max-width: calc(100vw - 1rem); }", app);
        Assert.Contains(".sk-formular { display: grid; grid-template-columns: minmax(0, 1fr);", app);
        Assert.Contains(".sk-karte-aktionen { display: flex; flex-wrap: wrap;", app);
        Assert.Contains(".pl-banner strong { display: block; margin-bottom: .15rem; overflow-wrap: anywhere; }", Css("css/plattform.css"));

        // Gebührenkarten (Sprachschule: « Anglais A1 – S1 · Session 1 seulement »): die Geltung bricht um, der Betrag bleibt in der Karte.
        var finanzen = Css("css/finanzen.css");
        Assert.Contains(".fin-posten-kopf > div { min-width: 0; }", finanzen);
        Assert.Contains(".fin-posten-kopf .sk-badge { white-space: normal;", finanzen);
        Assert.Contains(".fin-posten-raster { grid-template-columns: minmax(0, 1fr); }", finanzen);

        var js = File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "js", "skoulou.js"));
        Assert.Contains("function einpassen(fenster)", js);
        Assert.Contains("'.sk-auswahl-popup, .sk-datum-popup'", js);
    }

    /// <summary>
    /// Nutzer 16.11.2026: nach dem Tippen muss man sofort etwas sehen. Der Kreis des Servers kommt erst mit seiner Antwort;
    /// der Klick-Kreis erscheint gleich im Browser, für jeden « sk-btn » der interaktiven Seiten.
    /// </summary>
    [Fact]
    public void Knoepfe_zeigen_den_Kreis_sofort_beim_Tippen()
    {
        var js = File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "js", "skoulou.js"));
        Assert.Contains("window.__skKlickRing", js);
        Assert.Contains("closest('.sk-btn')", js);
        Assert.Contains("m.attributeName === 'tabindex'", js);   // Fokuswechsel ist keine Antwort des Servers
        Assert.Contains("knopf.hasAttribute('data-ohne-ladekreis')", js);
        Assert.Contains("wartend.setAttribute('data-sk-laedt'", js);   // gleiche Darstellung wie die klassischen Formulare
    }

    /// <summary>
    /// « Vidéos d'aide »: am Handy sprang die Seite nach einem Video im Querformat nach oben (die Seite ordnet sich beim Drehen
    /// um); jeder Nutzerbereich ist ein eigenes, farbig markiertes Feld mit « Votre rôle » bei der eigenen Rolle.
    /// </summary>
    [Fact]
    public void Videoseite_behaelt_ihre_Position_und_trennt_die_Nutzerbereiche()
    {
        var js = File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "js", "skoulou.js"));
        Assert.Contains("window.skVideoPosition", js);
        Assert.Contains("'orientationchange'", js);
        Assert.Contains("if (meiner !== lauf) return;", js);
        var seite = File.ReadAllText(Path.Combine(WebApp(), "Components", "Pages", "Aide", "Videos.razor"));
        Assert.Contains("\"skVideoPosition.merken\"", seite);
        Assert.Contains("\"skVideoPosition.zurueck\"", seite);
        Assert.Contains("class=\"sk-video-gruppe sk-video-rolle-@bereich.Klasse\"", seite);
        Assert.Contains("Votre rôle", seite);
        var css = File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "app.css"));
        foreach (var rolle in new[] { "tous", "direction", "enseignant", "eleve", "parent" })
            Assert.Contains($".sk-video-rolle-{rolle} {{ --vg-rgb:", css);
    }

    /// <summary>
    /// Mehr als zehn unbestätigte Zeichenschritte erlaubt: sonst wartet der Server beim Öffnen einer Seite auf den Browser
    /// (bei 400 ms Latenz 0,8 s ohne Daten – in S4 ging in dieser Lücke Getipptes verloren).
    /// </summary>
    [Fact]
    public void Verbindung_puffert_genug_Zeichenschritte()
    {
        var optionen = new Microsoft.AspNetCore.Components.Server.CircuitOptions();
        Assert.True(optionen.MaxBufferedUnacknowledgedRenderBatches <= 10, "Standard hat sich geändert – Einstellung prüfen");
        Skoulou.WebApp.Services.Infrastruktur.InfrastrukturRegistrierung.VerbindungsOptionen(optionen);
        Assert.True(optionen.MaxBufferedUnacknowledgedRenderBatches >= 40);

        var registrierung = File.ReadAllText(Path.Combine(WebApp(), "Services", "Infrastruktur", "InfrastrukturRegistrierung.cs"));
        Assert.Contains(".AddInteractiveServerComponents(VerbindungsOptionen)", registrierung);
    }

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
