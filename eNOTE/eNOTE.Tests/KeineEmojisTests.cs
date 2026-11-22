using System.Text.RegularExpressions;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Projektregel « Symbole über &lt;Icon&gt;, keine Emojis » (Fehlersuche 21.11.2026: 👁 im Passwortfeld, 📅 in der
/// Datumsauswahl, 🏪 im Ladenwechsler – je nach Gerät bunt, verschoben oder als Kästchen). Nutzerentscheidung 22.11.:
/// ersetzen. Erlaubt bleiben typografische Zeichen wie ✓ und die Begrüßung 👋 auf der Startseite (bewusst, aria-hidden).
/// </summary>
public class KeineEmojisTests
{
    private static string WebApp()
    {
        var ordner = new DirectoryInfo(AppContext.BaseDirectory);
        while (ordner is not null && !Directory.Exists(Path.Combine(ordner.FullName, "eNOTE", "eNOTE.WebApp"))) ordner = ordner.Parent;
        Assert.NotNull(ordner);
        return Path.Combine(ordner!.FullName, "eNOTE", "eNOTE.WebApp");
    }

    [Fact]
    public void Komponenten_Zeigen_Keine_Bild_Emojis()
    {
        var emoji = new Regex(@"\p{Cs}{2}|[☀-⛿]");
        var funde = Directory.EnumerateFiles(Path.Combine(WebApp(), "Components"), "*.razor", SearchOption.AllDirectories)
            .SelectMany(datei => File.ReadAllLines(datei).Select((zeile, i) => (datei, zeile, i)))
            .Where(x => emoji.IsMatch(x.zeile) && !x.zeile.Contains("👋"))
            .Select(x => $"{Path.GetFileName(x.datei)}:{x.i + 1}: {x.zeile.Trim()}")
            .ToList();
        Assert.True(funde.Count == 0, string.Join(Environment.NewLine, funde));
    }

    [Fact]
    public void Datumsauswahl_Und_Ladenwechsler_Nutzen_Icon()
    {
        var shared = Path.Combine(WebApp(), "Components", "Shared");
        Assert.Contains("<Icon Name=\"calendar\"", File.ReadAllText(Path.Combine(shared, "EnoteDatePicker.razor")));
        Assert.Contains("<Icon Name=\"store\"", File.ReadAllText(Path.Combine(shared, "SalonSwitcher.razor")));
    }
}
