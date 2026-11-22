using System.Text.RegularExpressions;
using ENote.WebApp.Services.Mails;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Befund B-6, Nutzerentscheidung 22.11.2026: Datum überall « 31/03/2028 » (ohne Jahr « 31/03 », mit Uhrzeit
/// « 31/03/2028 14:05 ») – in Oberfläche, PDFs und Mails. Vorher gemischt mit « 31.03.2028 ». Die Schrägstriche stehen
/// als Zeichen im Format (<c>dd'/'MM'/'yyyy</c>), damit die Anzeige nicht von der Kultur des Servers abhängt.
/// Ausgeschriebene Daten im Fließtext (« 1er octobre 2027 », « mercredi 11 novembre ») bleiben.
/// </summary>
public class DatumsFormatTests
{
    private static string Wurzel()
    {
        var ordner = new DirectoryInfo(AppContext.BaseDirectory);
        while (ordner is not null && !Directory.Exists(Path.Combine(ordner.FullName, "eNOTE", "eNOTE.WebApp"))) ordner = ordner.Parent;
        Assert.NotNull(ordner);
        return Path.Combine(ordner!.FullName, "eNOTE");
    }

    private static IEnumerable<string> Quellen() => new[] { "eNOTE.WebApp", "eNOTE.Core" }
        .SelectMany(p => Directory.EnumerateFiles(Path.Combine(Wurzel(), p), "*.*", SearchOption.AllDirectories))
        .Where(d => d.EndsWith(".cs") || d.EndsWith(".razor"))
        .Where(d => !Regex.IsMatch(d, @"[\\/](bin|obj|Migrations)[\\/]"));

    [Fact]
    public void Kein_Datum_Mit_Punkten_Und_Kein_Kulturabhaengiger_Schraegstrich()
    {
        var punkt = new Regex(@"(?<![A-Za-z'])dd\.MM|MM\.yy");
        var platzhalter = new Regex(@"(?<![A-Za-z'])dd/MM");
        var funde = Quellen()
            .SelectMany(d => File.ReadAllLines(d).Select((z, i) => (d, z, i)))
            .Where(x => !x.z.TrimStart().StartsWith("//") && (punkt.IsMatch(x.z) || platzhalter.IsMatch(x.z)))
            .Select(x => $"{Path.GetFileName(x.d)}:{x.i + 1}: {x.z.Trim()}")
            .ToList();
        Assert.True(funde.Count == 0, string.Join(Environment.NewLine, funde));
    }

    [Fact]
    public void Mails_Und_Hinweise_Zeigen_Das_Datum_Mit_Schraegstrichen()
    {
        var frist = new DateTime(2028, 3, 31, 23, 10, 0, DateTimeKind.Utc);
        var url = "https://enote.tablosuite.com/formation";
        foreach (var html in new[]
                 {
                     TabloSuiteMails.FormationOuverte("Nadège", frist, 80, 20, url),
                     TabloSuiteMails.FormationRappel("Nadège", frist, null, 80, url),
                     TabloSuiteMails.RappelPaiement("Ama", "Salon Test", 27750m, frist, "https://enote.tablosuite.com/abonnement"),
                 })
        {
            Assert.Contains("31/03/2028", html);
            Assert.DoesNotContain("31.03.2028", html);
        }
        // Unabhängig von der Kultur des Rechners (Tests laufen hier mit de-DE)
        Assert.Equal("31/03/2028 23:10", frist.ToString("dd'/'MM'/'yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("de-DE")));
    }
}
