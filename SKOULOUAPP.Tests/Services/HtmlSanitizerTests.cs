using SkoulouApp.Core.Services.Text;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>
/// Tests für <see cref="HtmlSanitizer"/>, die sicherstellen, dass erlaubte
/// Formatierung erhalten bleibt und gefährliche Inhalte entfernt werden.
/// </summary>
public class HtmlSanitizerTests
{
    [Fact]
    public void Sanitize_ErlaubteFormatierung_BleibtErhalten()
    {
        var eingabe = "<p><strong>Fett</strong> und <em>kursiv</em> mit <span class=\"ql-align-center\">Text</span></p>";

        var ergebnis = HtmlSanitizer.Sanitize(eingabe);

        Assert.Contains("<strong>", ergebnis);
        Assert.Contains("<em>", ergebnis);
        Assert.Contains("ql-align-center", ergebnis);
    }

    [Fact]
    public void Sanitize_SkriptTag_WirdEntfernt()
    {
        var eingabe = "<p>Skoulou</p><script>alert('xss')</script>";

        var ergebnis = HtmlSanitizer.Sanitize(eingabe);

        Assert.DoesNotContain("<script", ergebnis);
        Assert.DoesNotContain("alert", ergebnis);
    }

    [Fact]
    public void Sanitize_EventHandler_WirdEntfernt()
    {
        var eingabe = "<p onclick=\"steal()\">Klick</p><img src=\"x\" onerror=\"alert(1)\" />";

        var ergebnis = HtmlSanitizer.Sanitize(eingabe);

        Assert.DoesNotContain("onclick", ergebnis);
        Assert.DoesNotContain("onerror", ergebnis);
        // img ist nicht in der Allowlist und wird als Tag entfernt.
        Assert.DoesNotContain("<img", ergebnis);
    }

    [Fact]
    public void Sanitize_JavascriptLink_WirdEntfernt()
    {
        var eingabe = "<a href=\"javascript:alert(1)\">Link</a>";

        var ergebnis = HtmlSanitizer.Sanitize(eingabe);

        Assert.DoesNotContain("javascript:", ergebnis);
    }

    [Fact]
    public void Sanitize_ErlaubterLink_BehaeltHttpsUndErgaenztRel()
    {
        var eingabe = "<a href=\"https://example.com\" target=\"_blank\">Link</a>";

        var ergebnis = HtmlSanitizer.Sanitize(eingabe);

        Assert.Contains("https://example.com", ergebnis);
        Assert.Contains("target=\"_blank\"", ergebnis);
        Assert.Contains("noopener", ergebnis);
    }

    [Fact]
    public void Sanitize_ErlaubterLink_ErgaenztTargetBlank()
    {
        var eingabe = "<a href=\"https://example.com\">Link</a>";

        var ergebnis = HtmlSanitizer.Sanitize(eingabe);

        Assert.Contains("target=\"_blank\"", ergebnis);
        Assert.Contains("rel=\"noopener noreferrer\"", ergebnis);
    }

    [Fact]
    public void Sanitize_GefaehrlicherStil_WirdEntfernt()
    {
        var eingabe = "<p style=\"color: red; background: url(javascript:alert(1))\">Text</p>";

        var ergebnis = HtmlSanitizer.Sanitize(eingabe);

        Assert.Contains("color: red", ergebnis);
        Assert.DoesNotContain("url(", ergebnis);
        Assert.DoesNotContain("javascript:", ergebnis);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sanitize_LeereEingabe_LiefertLeerenString(string? eingabe)
    {
        var ergebnis = HtmlSanitizer.Sanitize(eingabe);

        Assert.Equal(string.Empty, ergebnis);
    }

    [Fact]
    public void SanitizeMitBildern_EingebettetesBild_BleibtErhalten()
    {
        var eingabe = "<p>Text</p><img src=\"data:image/png;base64,iVBORw0KGgo=\" alt=\"Bild\" width=\"200\" />";

        var ergebnis = HtmlSanitizer.SanitizeMitBildern(eingabe);

        Assert.Contains("<img", ergebnis);
        Assert.Contains("data:image/png;base64", ergebnis);
        Assert.Contains("alt=\"Bild\"", ergebnis);
        Assert.Contains("width=\"200\"", ergebnis);
    }

    [Fact]
    public void SanitizeMitBildern_SpeichertBildbreiteUndEntferntUnsichereAngaben()
    {
        var id = Guid.NewGuid();
        var bild = $"<img src=\"/media-library/{id}\" alt=\"Photo\" width=\"230\" onload=\"alert(1)\">";

        var ergebnis = HtmlSanitizer.SanitizeMitBildern(bild);

        Assert.Contains("width=\"230\"", ergebnis);
        Assert.Contains($"src=\"/media-library/{id}\"", ergebnis);
        Assert.DoesNotContain("onload", ergebnis);
        Assert.DoesNotContain("width=", HtmlSanitizer.SanitizeMitBildern($"<img src=\"/media-library/{id}\" width=\"calc(100%)\">"));
    }

    [Fact]
    public void SanitizeMitBildern_BewahrtAusrichtungDesBildes()
    {
        var id = Guid.NewGuid();
        var eingabe = $"<img src=\"/media-library/{id}\" width=\"230\" class=\"ql-image-align-right malicious\">";

        var ergebnis = HtmlSanitizer.SanitizeMitBildern(eingabe);

        Assert.Contains("ql-image-align-right", ergebnis);
        Assert.Contains("width=\"230\"", ergebnis);
        Assert.DoesNotContain("malicious", ergebnis);
    }

    [Fact]
    public void SanitizeMitBildern_BewahrtNumerischeBestandsbildStyles()
    {
        var id = Guid.NewGuid();
        var ergebnis = HtmlSanitizer.SanitizeMitBildern($"<img src=\"/media-library/{id}\" style=\"width: 180px; height: 120px; width: calc(100%); background-image: url(javascript:alert(1))\">");

        Assert.Contains("width: 180px", ergebnis);
        Assert.Contains("height: 120px", ergebnis);
        Assert.DoesNotContain("calc(", ergebnis);
        Assert.DoesNotContain("javascript:", ergebnis);
    }

    [Fact]
    public void SanitizeMitBildern_HttpsBild_BleibtErhalten()
    {
        var eingabe = "<img src=\"https://example.com/bild.png\" />";

        var ergebnis = HtmlSanitizer.SanitizeMitBildern(eingabe);

        Assert.Contains("<img", ergebnis);
        Assert.Contains("https://example.com/bild.png", ergebnis);
    }

    [Fact]
    public void SanitizeMitBildern_PrivateMedienId_BleibtErhalten_AberKeineFremdpfade()
    {
        var id = Guid.NewGuid();
        Assert.Contains($"src=\"/media-library/{id}\"", HtmlSanitizer.SanitizeMitBildern($"<img src=\"/media-library/{id}\">"));
        Assert.DoesNotContain("src=", HtmlSanitizer.SanitizeMitBildern("<img src=\"/media-library/../../secret\">"));
    }

    [Fact]
    public void SanitizeMitBildern_GefaehrlicheBildQuelleUndHandler_WerdenEntfernt()
    {
        var eingabe = "<img src=\"javascript:alert(1)\" onerror=\"alert(1)\" />";

        var ergebnis = HtmlSanitizer.SanitizeMitBildern(eingabe);

        Assert.DoesNotContain("javascript:", ergebnis);
        Assert.DoesNotContain("onerror", ergebnis);
    }

    [Fact]
    public void Sanitize_OhneBildModus_EntferntBild()
    {
        var eingabe = "<p>Text</p><img src=\"data:image/png;base64,iVBORw0KGgo=\" />";

        var ergebnis = HtmlSanitizer.Sanitize(eingabe);

        Assert.DoesNotContain("<img", ergebnis);
        Assert.Contains("Text", ergebnis);
    }
}
