using HalloApp.Core.Services;
using Xunit;

namespace HalloApp.Tests.Services;

/// <summary>
/// Tests für <see cref="AltersPruefung"/>.
/// </summary>
public class AltersPruefungTests
{
    [Theory]
    [InlineData(2000, 6, 15, 2025, 6, 15, 25)]
    [InlineData(2000, 6, 15, 2025, 6, 14, 24)]
    [InlineData(2000, 6, 15, 2025, 6, 16, 25)]
    public void BerechneAlter_LiefertVolleJahre(int gy, int gm, int gd, int sy, int sm, int sd, int erwartet)
    {
        var alter = AltersPruefung.BerechneAlter(new DateTime(gy, gm, gd), new DateTime(sy, sm, sd));

        Assert.Equal(erwartet, alter);
    }
}
