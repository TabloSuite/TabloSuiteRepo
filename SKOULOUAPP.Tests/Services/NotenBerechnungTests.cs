using SkoulouApp.Core.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class NotenBerechnungTests
{
    [Fact]
    public void Fachnote_NormalisiertBarèmesUndGewichtet()
    {
        Assert.Equal(15m, NotenBerechnung.FachMittel([new(5, 10, 1), new(35, 40, 2)]));
    }

    [Fact]
    public void FehlendeNoten_SindNichtNullPunkte()
    {
        Assert.Null(NotenBerechnung.FachMittel([new(0, 20, 1), new(null, 20, 1)]));
        Assert.Equal(0m, NotenBerechnung.FachMittel([new(0, 20, 1)]));
        Assert.Null(NotenBerechnung.GesamtMittel([(12m, 2m), (null, 1m)]));
    }

    [Fact]
    public void Jahresnote_SchliesstTestsImmerAus()
    {
        Assert.Equal(12m, NotenBerechnung.JahresMittel([new(10, 1, false), new(12, 1, false), new(14, 1, false), new(20, 100, true)]));
        Assert.Null(NotenBerechnung.JahresMittel([new(10, 1, false), new(null, 1, false)]));
    }

    [Fact]
    public void UngueltigeWerte_WerdenNichtBerechnet()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NotenBerechnung.FachMittel([new(21, 20, 1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => NotenBerechnung.GesamtMittel([(10m, 0m)]));
    }
}
