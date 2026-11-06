using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

public class FinanzZeitraumTests
{
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Zeitraum_Verwendet_Vollstaendige_Togo_Kalendertage(DateTimeKind art)
    {
        var von = new DateTime(2026, 11, 5, 14, 30, 0, art);
        var bis = new DateTime(2026, 11, 5, 16, 0, 0, art);

        Assert.True(FinanzZeitraum.TryErstellen(von, bis, out var zeitraum, out var fehler));

        Assert.Null(fehler);
        Assert.NotNull(zeitraum);
        Assert.Equal(new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc), zeitraum!.VonUtc);
        Assert.Equal(new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), zeitraum.BisExklusivUtc);
        Assert.Equal(DateTimeKind.Utc, zeitraum.VonUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, zeitraum.BisExklusivUtc.Kind);
        Assert.Equal(von.Date, zeitraum.Bis);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Zeitraum_Verlangt_Beide_Daten(bool fehltVon, bool fehltBis)
    {
        Assert.False(FinanzZeitraum.TryErstellen(fehltVon ? null : DateTime.UtcNow.Date,
            fehltBis ? null : DateTime.UtcNow.Date, out var zeitraum, out var fehler));
        Assert.Null(zeitraum);
        Assert.Equal("Veuillez sélectionner les deux dates.", fehler);
    }

    [Fact]
    public void Zeitraum_Lehnt_Umgekehrte_Daten_Ab()
    {
        Assert.False(FinanzZeitraum.TryErstellen(new DateTime(2026, 11, 6), new DateTime(2026, 11, 5), out _, out var fehler));
        Assert.Equal("La date de début doit être antérieure ou égale à la date de fin.", fehler);
    }

    [Fact]
    public void Zeitraum_Lehnt_Nicht_Darstellbare_Endgrenze_Ab()
    {
        Assert.False(FinanzZeitraum.TryErstellen(DateTime.UtcNow, DateTime.MaxValue, out var zeitraum, out var fehler));
        Assert.Null(zeitraum);
        Assert.Equal("La date de fin est trop éloignée.", fehler);
    }

    [Fact]
    public void Zeitraum_Ueberschreitet_Monats_Und_Jahresgrenzen()
    {
        Assert.True(FinanzZeitraum.TryErstellen(new DateTime(2026, 12, 31), new DateTime(2026, 12, 31), out var zeitraum, out _));
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), zeitraum!.BisExklusivUtc);
    }
}
