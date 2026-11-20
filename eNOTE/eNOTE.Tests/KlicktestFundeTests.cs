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

    [Fact]
    public void Admin_Responsable_Waehlen_Laedt_Die_Zuweisbaren_Laeden()
    {
        // Seit 9c9a229 blieb « établissements de … » leer (« Aucun établissement disponible pour une affectation »),
        // bis der Admin etwas ins Suchfeld tippte: Läden konnten so praktisch nicht mehr zugewiesen werden.
        var rumpf = Methode(Path.Combine(WebApp(), "Components", "Pages", "Admin.razor.cs"), "WaehleProduktOwnerAsync");
        Assert.Matches(new Regex(@"await\s+LadePoSalonsAsync\(\)"), rumpf);
    }
}
