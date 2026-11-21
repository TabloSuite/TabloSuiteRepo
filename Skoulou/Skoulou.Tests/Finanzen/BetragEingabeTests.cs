using Skoulou.Core.Finanzen;

namespace Skoulou.Tests.Finanzen;

/// <summary>Betragseingabe in ganzen FCFA (BetragFeld). Klicktest 21.11.2026: « 15000.00 » wurde still zu 1.500.000 FCFA.</summary>
public class BetragEingabeTests
{
    [Theory]
    [InlineData("150000", 150_000)]
    [InlineData("150.000", 150_000)]
    [InlineData("1.500.000", 1_500_000)]
    [InlineData("150 000", 150_000)]
    [InlineData("150 000", 150_000)]
    [InlineData("150 000", 150_000)]
    [InlineData(" 25000 ", 25_000)]
    [InlineData("0", 0)]
    [InlineData("999.999.999", 999_999_999)]
    public void Gueltige_Betraege(string eingabe, long erwartet)
    {
        Assert.Equal(BetragStatus.Ok, BetragEingabe.Lies(eingabe, out var wert));
        Assert.Equal(erwartet, wert);
    }

    [Theory]
    [InlineData("15000.00")]
    [InlineData("15000.5")]
    [InlineData("1.5")]
    [InlineData("15.00")]
    [InlineData("1500.000")]
    [InlineData("15000,50")]
    [InlineData("-500")]
    [InlineData("12a")]
    [InlineData("1..000")]
    [InlineData(".500")]
    [InlineData("500.")]
    public void Dezimalstellen_und_Fremdzeichen_sind_Fehleingaben(string eingabe)
        => Assert.Equal(BetragStatus.Format, BetragEingabe.Lies(eingabe, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Leer_ist_kein_Betrag(string? eingabe) => Assert.Equal(BetragStatus.Leer, BetragEingabe.Lies(eingabe, out _));

    [Fact]
    public void Ueberlauf_wird_gemeldet() => Assert.Equal(BetragStatus.ZuGross, BetragEingabe.Lies("99999999999999999999", out _));
}
