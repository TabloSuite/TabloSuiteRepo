using SkoulouApp.Core.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>Einheitliche Anzeige wie bei eNOTE, unabhängig von der Serversprache.</summary>
public class SkoulouKulturTests
{
    private static readonly System.Globalization.CultureInfo K = SkoulouKultur.Instanz;

    [Fact]
    public void Betraege_Mit_Punkt_Als_Tausendertrennzeichen()
        => Assert.Equal("150.000", 150000m.ToString("N0", K));

    [Fact]
    public void Dezimalzahlen_Mit_Komma()
        => Assert.Equal("13,67", 13.666m.ToString("0.00", K));

    [Fact]
    public void Datum_Und_Wochentag_Auf_Franzoesisch()
    {
        var tag = new DateTime(2026, 11, 12);
        Assert.Equal("12/11/2026", tag.ToString("d", K));
        Assert.Equal("jeudi 12 novembre 2026", tag.ToString("dddd d MMMM yyyy", K));
    }

    [Fact]
    public void Instanz_Ist_Schreibgeschuetzt()
        => Assert.True(K.IsReadOnly);
}
