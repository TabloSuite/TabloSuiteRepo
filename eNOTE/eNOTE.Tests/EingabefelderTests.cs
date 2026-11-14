using System.Text.RegularExpressions;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Klick-Simulation S2: Mit <c>value="@x" @oninput="…"</c> schreibt Blazor Server jede Antwort ungeprüft in das Feld zurück.
/// Bei langsamer Verbindung (Togo: 250–400 ms) wurden so getippte Zeichen überschrieben – Passwort bei der Anmeldung,
/// Kassen-PIN, Beträge, Kundensuche (Kunden mit verstümmeltem Namen), Motif und Notizen. Nur <c>@bind</c> bzw.
/// <c>@bind:get/@bind:set</c> gleicht mit dem Stand im Browser ab. Dieser Test verbietet das unsichere Muster.
/// </summary>
public class EingabefelderTests
{
    [Fact]
    public void Kein_Eingabefeld_kombiniert_value_mit_oninput()
    {
        var komponenten = Path.Combine(RepoWurzel(), "eNOTE", "eNOTE.WebApp", "Components");
        var funde = new List<string>();
        foreach (var datei in Directory.EnumerateFiles(komponenten, "*.razor", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(datei);
            foreach (Match m in Regex.Matches(text, @"<(input|textarea)\b[^>]*>", RegexOptions.Singleline))
            {
                var tag = m.Value;
                if (tag.Contains("@oninput", StringComparison.Ordinal) && Regex.IsMatch(tag, @"\bvalue\s*=\s*""@"))
                    funde.Add($"{Path.GetFileName(datei)}: {Regex.Replace(tag, @"\s+", " ")[..Math.Min(120, tag.Length)]}");
            }
        }
        Assert.True(funde.Count == 0, "Unsicheres Muster value + @oninput: " + string.Join(" | ", funde));
    }

    [Theory]
    [InlineData("15000", 15000)]
    [InlineData("1 525 000", 1525000)]
    [InlineData("1.200", 1200)]
    [InlineData("", 0)]
    public void Betragseingabe_liest_den_getippten_Text_als_Ziffern(string text, decimal erwartet)
        => Assert.Equal(erwartet, ENote.WebApp.Components.Shared.BetragEingabe.ZifferWert(text));

    // Monkey S2: eine Remise « 0 % » mit Unsinn-Namen wurde gespeichert (an der Kasse wählbar, ohne Wirkung).
    [Theory]
    [InlineData(ENote.Core.DataModels.Katalog.RabattArt.Prozent, 0, false)]
    [InlineData(ENote.Core.DataModels.Katalog.RabattArt.Prozent, -5, false)]
    [InlineData(ENote.Core.DataModels.Katalog.RabattArt.Prozent, 101, false)]
    [InlineData(ENote.Core.DataModels.Katalog.RabattArt.Prozent, 10, true)]
    [InlineData(ENote.Core.DataModels.Katalog.RabattArt.Festbetrag, 0, false)]
    [InlineData(ENote.Core.DataModels.Katalog.RabattArt.Festbetrag, 500, true)]
    public void Remise_braucht_einen_Wert_ueber_null(ENote.Core.DataModels.Katalog.RabattArt art, int wert, bool gueltig)
        => Assert.Equal(gueltig, ENote.WebApp.Components.Pages.Rabatte.Pruefe(new ENote.Core.DataModels.Katalog.Rabatt { Name = "Fidélité", Art = art, Wert = wert }) is null);

    private static string RepoWurzel([System.Runtime.CompilerServices.CallerFilePath] string quelle = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(quelle)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "eNOTE", "eNOTE.WebApp", "Program.cs")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo-Wurzel nicht gefunden.");
    }
}
