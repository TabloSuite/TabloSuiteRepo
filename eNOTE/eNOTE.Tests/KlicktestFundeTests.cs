using System.Text.RegularExpressions;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Befunde aus dem Klicktest vom 20.11.2026 (Browser, wie ein Mensch geklickt), die sich nur über die Verdrahtung der
/// Oberfläche zeigen. Fachliche Teile stehen in den jeweiligen Service-Tests.
/// </summary>
public class KlicktestFundeTests
{
    private static string WebApp([System.Runtime.CompilerServices.CallerFilePath] string quelle = "")
        => Path.Combine(Path.GetDirectoryName(quelle)!, "..", "eNOTE.WebApp");

    private static string Methode(string datei, string name)
    {
        var text = File.ReadAllText(datei);
        var start = text.IndexOf($"Task {name}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Methode {name} nicht gefunden");
        var auf = text.IndexOf('{', start);
        var tiefe = 0;
        for (var i = auf; i < text.Length; i++)
        {
            if (text[i] == '{') tiefe++;
            else if (text[i] == '}' && --tiefe == 0) return text[auf..(i + 1)];
        }
        throw new InvalidOperationException("Methodenende nicht gefunden");
    }

    [Theory]
    [InlineData(1500, null, true)]
    [InlineData(1.5, null, false)]      // « 1.500 » im Zahlenfeld des Browsers
    [InlineData(2500, 1.2, false)]      // Prix d'achat « 1.200 »
    [InlineData(0, 900.0, true)]
    public void Katalogpreise_Sind_Ganze_Francs(decimal preis, double? kaufpreis, bool ok)
        => Assert.Equal(ok, ENote.WebApp.Services.PreisEingabe.Pruefe(preis, (decimal?)kaufpreis) is null);

    [Fact]
    public void Prestation_Speichern_Prueft_Ganze_Francs()
    {
        var rumpf = Methode(Path.Combine(WebApp(), "Components", "Pages", "Leistungen.razor.cs"), "SpeichernAsync");
        Assert.Contains("PreisEingabe.Pruefe(bearbeitungsLeistung.Preis, bearbeitungsLeistung.Kaufpreis)", rumpf);
    }

    [Fact]
    public void Vorgerenderte_Seiten_Zeigen_Verbindungsaufbau_Und_Fangen_Klicks_Ab()
    {
        // Langsames Netz (700 ms): Kasse nach 3 s sichtbar, erst nach 13 s bedienbar – Tippen ging still verloren.
        var routes = File.ReadAllText(Path.Combine(WebApp(), "Components", "Routes.razor"));
        Assert.Contains("AssignedRenderMode is not null && !RendererInfo.IsInteractive", routes);
        Assert.Contains("data-enote-vorgerendert", routes);
        Assert.Contains("Connexion en cours…", routes);
        var js = File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "js", "enote.js"));
        Assert.Contains("document.querySelector('[data-enote-vorgerendert]')", js);
        Assert.Contains("window.addEventListener(typ, abfangen, true)", js);
        Assert.Contains(".enote-verbindung-aufbau.sichtbar", File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "app.css")));
    }

    [Fact]
    public void Admin_Responsable_Waehlen_Laedt_Die_Zuweisbaren_Laeden()
    {
        // Seit 9c9a229 blieb « établissements de … » leer (« Aucun établissement disponible pour une affectation »),
        // bis der Admin etwas ins Suchfeld tippte: Läden konnten so praktisch nicht mehr zugewiesen werden.
        var rumpf = Methode(Path.Combine(WebApp(), "Components", "Pages", "Admin.razor.cs"), "WaehleProduktOwnerAsync");
        Assert.Matches(new Regex(@"await\s+LadePoSalonsAsync\(\)"), rumpf);
    }
}
