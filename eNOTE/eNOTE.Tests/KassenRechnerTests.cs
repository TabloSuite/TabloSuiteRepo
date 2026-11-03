using ENote.Core.DataModels.Katalog;
using ENote.Core.Services.Kasse;
using Xunit;

namespace ENote.Tests;

public class KassenRechnerTests
{
    [Theory]
    [InlineData(10000, 10, 9000)]   // 10 % von 10000 = 1000 Rabatt
    [InlineData(5000, 0, 5000)]     // 0 % bleibt
    [InlineData(5000, 100, 0)]      // 100 % = 0
    [InlineData(5000, 150, 0)]      // über 100 % wird auf 100 % gedeckelt
    public void WendeRabattAn_Prozent(decimal betrag, decimal prozent, decimal erwartet)
        => Assert.Equal(erwartet, KassenRechner.WendeRabattAn(betrag, RabattArt.Prozent, prozent));

    [Theory]
    [InlineData(10000, 1500, 8500)]
    [InlineData(1000, 2000, 0)]     // Festbetrag über dem Betrag fällt nicht unter 0
    [InlineData(1000, -500, 1000)]  // negativer Festbetrag wird ignoriert
    public void WendeRabattAn_Festbetrag(decimal betrag, decimal festbetrag, decimal erwartet)
        => Assert.Equal(erwartet, KassenRechner.WendeRabattAn(betrag, RabattArt.Festbetrag, festbetrag));

    [Fact]
    public void BerechneRueckgeld_Erfolgreich()
        => Assert.Equal(500m, KassenRechner.BerechneRueckgeld(4500, 5000));

    [Fact]
    public void BerechneRueckgeld_Exakt_0()
        => Assert.Equal(0m, KassenRechner.BerechneRueckgeld(5000, 5000));

    [Fact]
    public void BerechneRueckgeld_Wirft_Bei_Unterdeckung()
        => Assert.Throws<InvalidOperationException>(() => KassenRechner.BerechneRueckgeld(5000, 4000));

    [Fact]
    public void BerechneBarSoll_Nur_Bar_Einnahmen()
    {
        var verkaeufe = new List<Core.DataModels.Kasse.Verkauf>
        {
            new() { ZahlungsArt = Core.DataModels.Kasse.ZahlungsArt.Bar, GesamtSumme = 3000 },
            new() { ZahlungsArt = Core.DataModels.Kasse.ZahlungsArt.Karte, GesamtSumme = 2000 },
            new() { ZahlungsArt = Core.DataModels.Kasse.ZahlungsArt.Bar, GesamtSumme = 1000 }
        };

        // Start 5000 + Bar 4000 = 9000; Karte zählt nicht zur Kasse.
        Assert.Equal(9000m, KassenRechner.BerechneBarSoll(5000, verkaeufe));
    }

    [Fact]
    public void BerechneDifferenz_Positiv_Und_Negativ()
    {
        Assert.Equal(200m, KassenRechner.BerechneDifferenz(9000, 9200));
        Assert.Equal(-300m, KassenRechner.BerechneDifferenz(9000, 8700));
    }
}
