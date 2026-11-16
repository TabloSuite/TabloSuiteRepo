using Xunit;

namespace ENote.Tests;

/// <summary>
/// Nutzer 16.11.2026: « Es ist blöd, wenn man nach dem Klick nichts merkt. » Der Ladekreis des Servers erscheint erst mit
/// seiner Antwort (bei langsamer Verbindung spürbar spät); fehlende Pflichtfelder der Registrierung wurden gar nicht
/// gemeldet. Jetzt: Kreis sofort beim Tippen auf jeden Aktionsknopf, Meldungen direkt über dem Knopf.
/// </summary>
public class LadekreisTests
{
    private static string WebApp()
    {
        var ordner = new DirectoryInfo(AppContext.BaseDirectory);
        while (ordner is not null && !Directory.Exists(Path.Combine(ordner.FullName, "eNOTE", "eNOTE.WebApp"))) ordner = ordner.Parent;
        Assert.NotNull(ordner);
        return Path.Combine(ordner!.FullName, "eNOTE", "eNOTE.WebApp");
    }

    [Fact]
    public void Aktionsknoepfe_zeigen_den_Kreis_sofort_beim_Tippen()
    {
        var js = File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "js", "enote.js"));
        Assert.Contains("window.__enoteKlickRing", js);
        Assert.Contains("closest('.enote-btn, .btn-primary')", js);          // Kacheln und Tasten der Kasse bleiben frei
        Assert.Contains("m.attributeName === 'tabindex'", js);                // Fokuswechsel ist keine Antwort des Servers
        Assert.Contains("knopf.hasAttribute('data-ohne-ladekreis')", js);
        Assert.Contains("form.hasAttribute('data-ladeanzeige')", js);         // klassische Formulare behalten ihre eigene Anzeige
        var css = File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "app.css"));
        Assert.Contains(".enote-btn[data-enote-wartet], .btn-primary[data-enote-wartet] { pointer-events: none;", css);
        Assert.Contains("content: attr(data-enote-wartet);", css);
        // Die Kacheln der Kasse sind keine « enote-btn »: schnelles Mehrfachtippen wird nicht gebremst.
        var kasse = File.ReadAllText(Path.Combine(WebApp(), "Components", "Pages", "Caisse.razor"));
        Assert.Contains("class=\"kx-kachel", kasse);
        Assert.DoesNotContain("kx-kachel enote-btn", kasse);
    }

    [Fact]
    public void Registrierung_meldet_fehlende_Felder_direkt_ueber_dem_Knopf()
    {
        var seite = File.ReadAllText(Path.Combine(WebApp(), "Components", "Pages", "Register.razor"));
        var summe = seite.IndexOf("<ValidationSummary />", StringComparison.Ordinal);
        var fehler = seite.IndexOf("<div class=\"alert alert-error\" role=\"alert\">@errorMessage</div>", StringComparison.Ordinal);
        var knopf = seite.IndexOf("<button type=\"submit\" class=\"btn-primary\"", StringComparison.Ordinal);
        Assert.True(summe > 0 && fehler > summe && knopf > fehler, "Meldungen müssen unmittelbar vor dem Absende-Knopf stehen");
        Assert.Contains(".auth-form .validation-errors {", File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "app.css")));
    }
}
