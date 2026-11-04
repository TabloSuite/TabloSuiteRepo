using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

public class EtikettPdfServiceTests
{
    [Fact]
    public void ErzeugeA4Bogen_LiefertPdf()
    {
        var etikett = new EtikettPdfService.Etikett("ABC123", "Shampoo", "1 500 F");
        var pdf = EtikettPdfService.ErzeugeA4Bogen([etikett, etikett, etikett]);
        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 500);
        Assert.Equal(0x25, pdf[0]); // %PDF
    }

    [Fact]
    public void ErzeugeA4Bogen_48Etiketten_Funktioniert()
    {
        var etikett = new EtikettPdfService.Etikett("X9", "Test", null);
        var pdf = EtikettPdfService.ErzeugeA4Bogen(Enumerable.Repeat(etikett, 48).ToList());
        Assert.True(pdf.Length > 500);
    }

    [Fact]
    public void ErzeugeEinzelEtikett_LiefertPdf()
    {
        var etikett = new EtikettPdfService.Etikett("XYZ789", "Crème", "2 000 F");
        var pdf = EtikettPdfService.ErzeugeEinzelEtikett(etikett);
        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 500);
        Assert.Equal(0x25, pdf[0]);
    }
}
