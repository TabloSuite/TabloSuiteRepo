using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.Services;
using FinanzBerichtPage = SkoulouApp.WebApp.Components.Pages.FinanzBericht;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class FinanzBerichtQueryTests
{
    [Theory]
    [InlineData("Einnahme", FinanzRichtung.Einnahme)]
    [InlineData("ausgabe", FinanzRichtung.Ausgabe)]
    public void ParseDirection_AkzeptiertGueltigeFilter(string value, FinanzRichtung expected)
    {
        Assert.Equal(expected, FinanzBerichtPage.ParseDirection(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unbekannt")]
    [InlineData("42")]
    public void ParseDirection_LehntUnbekannteFilterAb(string value)
    {
        Assert.Null(FinanzBerichtPage.ParseDirection(value));
    }

    [Theory]
    [InlineData("BetragAbsteigend", FinanzSortierung.BetragAbsteigend)]
    [InlineData("wirkungsdatumaufsteigend", FinanzSortierung.WirkungsdatumAufsteigend)]
    public void ParseSort_AkzeptiertGueltigeSortierung(string value, FinanzSortierung expected)
    {
        Assert.Equal(expected, FinanzBerichtPage.ParseSort(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unbekannt")]
    [InlineData("42")]
    public void ParseSort_LehntUnbekannteSortierungAb(string value)
    {
        Assert.Null(FinanzBerichtPage.ParseSort(value));
    }
}
