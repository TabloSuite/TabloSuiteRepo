using System.Xml.Linq;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

public class EtikettPdfServiceTests
{
    [Theory]
    [InlineData("123456789012")]
    [InlineData("ARTICLE-001")]
    public void Barcode_Wird_Als_Svg_Mit_Balken_Erzeugt(string code)
    {
        var svg = EtikettPdfService.ErzeugeCode128(code);
        Assert.NotNull(svg);
        var dokument = XDocument.Parse(svg);
        Assert.Equal("svg", dokument.Root!.Name.LocalName);
        Assert.True(dokument.Descendants().Count(element => element.Name.LocalName == "rect") > 1);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Leerer_Code_Erzeugt_Keinen_Barcode(string code)
        => Assert.Null(EtikettPdfService.ErzeugeCode128(code));

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

    [Fact]
    public void Einzel_Etiketten_mehrfach_und_mit_Hersteller_QR()
    {
        var etikett = new EtikettPdfService.Etikett("HTTPS://ID.GS1.ORG/01/09506000134352/10/ABC123?17=271231", "Huile", "1 500 F");
        var einmal = EtikettPdfService.ErzeugeEinzelEtikett(etikett);
        var dreimal = EtikettPdfService.ErzeugeEinzelEtikett(etikett, 3);
        Assert.Equal(0x25, dreimal[0]);
        Assert.True(dreimal.Length > einmal.Length);
    }
}
