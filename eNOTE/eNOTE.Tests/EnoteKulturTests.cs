using ENote.Core.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>Gleiche Anzeige auf jedem Server: Punkt als Tausendertrenner, Komma für Dezimalstellen, Datum mit « / ».</summary>
public class EnoteKulturTests
{
    [Fact]
    public void Zahlen_Und_Daten_Werden_Wie_In_Der_App_Angezeigt()
    {
        var k = EnoteKultur.Erstelle();
        Assert.Equal("5.000", 5000m.ToString("N0", k));
        Assert.Equal("147.500", 147500m.ToString("N0", k));
        Assert.Equal("7,5", 7.5m.ToString("0.#", k));
        Assert.Equal("11/11/2026", new DateTime(2026, 11, 11).ToString("dd/MM/yyyy", k));
        Assert.Equal("mercredi", new DateTime(2026, 11, 11).ToString("dddd", k));
        Assert.Equal("09:05", new DateTime(2026, 11, 11, 9, 5, 0).ToString("HH:mm", k));
    }

    [Fact]
    public void Programm_Setzt_Die_Kultur_Fuer_Alle_Anfragen()
    {
        var program = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Quelle())!, "..", "eNOTE.WebApp", "Program.cs"));
        Assert.Contains("EnoteKultur.Erstelle()", program);
        Assert.Contains("UseRequestLocalization", program);
    }

    private static string Quelle([System.Runtime.CompilerServices.CallerFilePath] string pfad = "") => pfad;
}
