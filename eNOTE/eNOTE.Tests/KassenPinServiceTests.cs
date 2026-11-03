using ENote.Core.Services.Kasse;
using Xunit;

namespace ENote.Tests;

public class KassenPinServiceTests
{
    [Fact]
    public void HashPin_Und_PruefePin_Rundreise()
    {
        var hash = KassenPinService.HashPin("4821");
        Assert.True(KassenPinService.PruefePin("4821", hash));
        Assert.False(KassenPinService.PruefePin("4822", hash));
    }

    [Fact]
    public void HashPin_Erzeugt_Unterschiedliche_Hashes_Bei_Gleicher_Pin()
    {
        var h1 = KassenPinService.HashPin("1234");
        var h2 = KassenPinService.HashPin("1234");
        Assert.NotEqual(h1, h2); // unterschiedlicher Salt
    }

    [Theory]
    [InlineData("1234", true)]
    [InlineData("123456", true)]
    [InlineData("123", false)]    // zu kurz
    [InlineData("1234567", false)]// zu lang
    [InlineData("12a4", false)]   // keine Ziffer
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IstGueltigesPinFormat(string? pin, bool erwartet)
        => Assert.Equal(erwartet, KassenPinService.IstGueltigesPinFormat(pin));

    [Fact]
    public void PruefePin_Bei_Beschaedigtem_Hash_False()
    {
        Assert.False(KassenPinService.PruefePin("1234", "kaputt"));
        Assert.False(KassenPinService.PruefePin("1234", "abc.def.ghi"));
    }
}
