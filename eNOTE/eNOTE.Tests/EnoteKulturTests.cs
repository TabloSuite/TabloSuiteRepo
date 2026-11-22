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

    /// <summary>Befund B-7, Nutzerentscheidung 22.11.2026: Mails zeigen Beträge wie die App (« 38.880 F », nicht « 38 880 F CFA »).</summary>
    [Fact]
    public void Betraege_In_Mails_Und_Bannern_Wie_In_Der_App()
    {
        Assert.Equal("38.880 F", EnoteKultur.Betrag(38880m));
        Assert.Equal("500 F", EnoteKultur.Betrag(500m));
        Assert.Equal("1.234.567 F", EnoteKultur.Betrag(1234567m));
        Assert.Equal("1.821 F", EnoteKultur.Betrag(1820.6m));
        Assert.Equal(EnoteKultur.Betrag(27750m), ENote.WebApp.Services.Mails.TabloSuiteMails.Betrag(27750m));

        // Alle Mail-Vorlagen mit Betrag: kein « F CFA », kein schmales Leerzeichen von fr-FR.
        var jetzt = new DateTime(2027, 8, 2, 0, 0, 0, DateTimeKind.Utc);
        var url = "https://enote.tablosuite.com/abonnement";
        var mails = new[]
        {
            ENote.WebApp.Services.Mails.TabloSuiteMails.RappelPaiement("Ama", "Salon Test", 27750m, jetzt, url),
            ENote.WebApp.Services.Mails.TabloSuiteMails.PaiementRefuse("Ama", "Salon Test", 38880m, url),
        };
        foreach (var html in mails)
        {
            Assert.DoesNotContain("F CFA", html);
            Assert.DoesNotContain(' ', html);
            Assert.DoesNotContain(' ' + "F", html);
        }
        Assert.Contains("27.750 F", mails[0]);
        Assert.Contains("38.880 F", mails[1]);
        Assert.Contains("4.000 F", ENote.WebApp.Services.PreisAnpassungService.Fuelle("{nouveau}", 3600m, 4000m, null));
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
