using System.Reflection;
using SkoulouApp.WebApp.Components.Shared;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class AzureBlobImageLibraryValidationTests
{
    [Theory]
    [InlineData("A/(/BBB", "A ( BBB")]
    [InlineData("  Ma carte (2026)  ", "Ma carte (2026)")]
    [InlineData("///", "")]
    public void NormalizeDisplayName_EntferntProblematischeZeichen(string input, string expected)
    {
        Assert.Equal(expected, AzureBlobImageLibrary.NormalizeDisplayName(input));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("Une carte historique", false)]
    public void UploadImage_AltTextPflichtfeldIstVorServiceaufrufValidiert(string altText, bool invalid)
    {
        var component = new AzureBlobImageLibrary();
        var field = typeof(AzureBlobImageLibrary).GetField("altText", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(component, altText);
        var method = typeof(AzureBlobImageLibrary).GetMethod("IsAltTextInvalid", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(invalid, method.Invoke(component, null));
    }

    [Fact]
    public void UploadImage_ZuLangerAltTextWirdAbgelehnt()
    {
        var component = new AzureBlobImageLibrary();
        var field = typeof(AzureBlobImageLibrary).GetField("altText", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(component, new string('a', 251));
        var method = typeof(AzureBlobImageLibrary).GetMethod("IsAltTextInvalid", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(true, method.Invoke(component, null));
    }
}
