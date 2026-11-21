using Skoulou.WebApp.Components.Shared;

namespace Skoulou.Tests.Plattform;

/// <summary>Klicktest 20.11.2026: Geburtsdatum nur per Monatsblättern = 152 Klicks → Monat und Jahr direkt wählbar.</summary>
public class DatumFeldTests
{
    private static readonly DateOnly Heute = new(2026, 11, 21);

    [Fact]
    public void Ohne_Grenzen_100_Jahre_zurueck_bis_10_voraus_neuestes_zuerst()
    {
        var jahre = DatumFeld.JahreFuer(null, null, Heute, 2026).ToList();
        Assert.Equal(2036, jahre[0]);
        Assert.Equal(1926, jahre[^1]);
        Assert.Contains(2014, jahre); // Geburtsjahr eines Schülers
        Assert.Equal(jahre.OrderByDescending(j => j), jahre);
    }

    [Fact]
    public void Mit_Grenzen_nur_deren_Jahre_angezeigtes_Jahr_immer_dabei()
    {
        Assert.Equal([2027, 2026], DatumFeld.JahreFuer(new DateOnly(2026, 9, 1), new DateOnly(2027, 7, 31), Heute, 2026));
        Assert.Equal([2027, 2026, 2025], DatumFeld.JahreFuer(new DateOnly(2026, 9, 1), new DateOnly(2027, 7, 31), Heute, 2025));
    }

    [Fact]
    public void Monatsnamen_franzoesisch()
    {
        Assert.Equal("janvier", DatumFeld.MonatsName(1));
        Assert.Equal("mars", DatumFeld.MonatsName(3));
        Assert.Equal("décembre", DatumFeld.MonatsName(12));
    }
}
