using SkoulouApp.Core.Options;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>Gemeinsames TabloSuite-Speicherkonto: nur saubere Konto-URI, eigener Container je Bereich.</summary>
public class SkoulouBlobSpeicherTests
{
    [Theory]
    [InlineData("https://tablosuite.blob.core.windows.net")]
    [InlineData("https://tablosuite.blob.core.windows.net/")]
    public void Konto_Uri_Wird_Akzeptiert(string uri)
        => Assert.Equal("https://tablosuite.blob.core.windows.net/", SkoulouBlobSpeicher.PruefeServiceUri(uri).ToString());

    [Theory]
    [InlineData("http://tablosuite.blob.core.windows.net")]
    [InlineData("https://tablosuite.blob.core.windows.net/?sv=2024&sig=abc")]
    [InlineData("https://tablosuite.blob.core.windows.net/container")]
    [InlineData("https://example.com")]
    public void Unsichere_Oder_Falsche_Uri_Wird_Abgelehnt(string uri)
        => Assert.Throws<InvalidOperationException>(() => SkoulouBlobSpeicher.PruefeServiceUri(uri));

    [Fact]
    public void Container_Liegt_Im_Gemeinsamen_Konto()
    {
        var options = new AzureBlobStorageOptions { ServiceUri = "https://tablosuite.blob.core.windows.net" };
        Assert.True(SkoulouBlobSpeicher.IstKonfiguriert(options));
        var container = SkoulouBlobSpeicher.Container(options, "skoulou-medien");
        Assert.Equal("https://tablosuite.blob.core.windows.net/skoulou-medien", container.Uri.ToString());
        Assert.False(SkoulouBlobSpeicher.IstKonfiguriert(new AzureBlobStorageOptions()));
    }
}
