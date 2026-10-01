using HalloApp.WebApp.Services;
using Xunit;

namespace HalloApp.Tests.Services;

/// <summary>
/// Tests für die gemeinsame E-Mail-Vorlage <see cref="EmailTemplates"/>.
/// Es wird ausschließlich HTML erzeugt und geprüft – es findet kein Versand statt.
/// </summary>
public class EmailTemplatesTests
{
    [Fact]
    public void Wrap_EnthaeltHeaderMitMarkeUndFooterMitKontakt()
    {
        var html = EmailTemplates.Wrap("Bienvenue", "<p>Bonjour</p>");

        // Header
        Assert.Contains("SKOULOUAPP", html);
        Assert.Contains("https://skoulou.etsetech.com", html);

        // Footer
        Assert.Contains("APPRENDRE. PROGRESSER. RÉUSSIR.", html);
        Assert.Contains("CONTACT", html);
        Assert.Contains("contact.skoulou@etsetech.com", html);
        Assert.Contains("+49 176 61221600", html);
        Assert.Contains("Lomé, Togo", html);
        Assert.Contains($"{DateTime.UtcNow.Year}", html);
    }

    [Fact]
    public void Wrap_UebernimmtUeberschriftUndInhalt()
    {
        var html = EmailTemplates.Wrap("Ma titre", "<p>Mon contenu unique</p>");

        Assert.Contains("Ma titre", html);
        Assert.Contains("Mon contenu unique", html);
    }

    [Fact]
    public void Wrap_NutztMarkenfarbenUndKeineViolettenAltfarben()
    {
        var html = EmailTemplates.Wrap("Titre", "<p>x</p>");

        Assert.Contains("#0b1b3a", html); // Navy im Header/Footer
        Assert.Contains("#38bdf8", html); // Akzent des Logo-Badges
        Assert.Contains("#f7faff", html); // leicht getönter Inhaltsbereich
        Assert.DoesNotContain("#7c3aed", html); // altes Violett
    }

    [Fact]
    public void Wrap_OhnePreheader_ErzeugtKeinenVersteckdiv()
    {
        var html = EmailTemplates.Wrap("Titre", "<p>x</p>");

        Assert.DoesNotContain("mso-hide:all", html);
    }

    [Fact]
    public void Wrap_MitPreheader_VerstecktDenVorschautext()
    {
        var html = EmailTemplates.Wrap("Titre", "<p>x</p>", "Aperçu de la boîte de réception");

        Assert.Contains("Aperçu de la boîte de réception", html);
        Assert.Contains("mso-hide:all", html);
    }

    [Fact]
    public void Wrap_NutztNurInlineStylesOhneStyleBlock()
    {
        var html = EmailTemplates.Wrap("Titre", "<p>x</p>");

        // <style>-Blöcke werden von vielen Clients entfernt und erhöhen den Spam-Score.
        Assert.DoesNotContain("<style", html);
    }

    [Fact]
    public void Button_ErzeugtVerlinktenCallToAction()
    {
        var html = EmailTemplates.Button("https://example.com/login", "Se connecter");

        Assert.Contains("https://example.com/login", html);
        Assert.Contains("Se connecter", html);
    }

    [Fact]
    public void InfoBox_UmschliesstDenInhalt()
    {
        var html = EmailTemplates.InfoBox("<strong>HalloPlus</strong>");

        Assert.Contains("<strong>HalloPlus</strong>", html);
        Assert.Contains("border-left", html);
    }

    [Fact]
    public void ToPlainText_EntferntMarkupUndDecodiertEntities()
    {
        var html = EmailTemplates.Wrap("Bienvenue", "<p>Bonjour Jean</p><p>&bull; HalloBasis</p>");

        var text = EmailTemplates.ToPlainText(html);

        Assert.DoesNotContain("<", text);
        Assert.DoesNotContain(">", text);
        Assert.Contains("Bonjour Jean", text);
        Assert.Contains("•", text);
    }

    [Fact]
    public void ToPlainText_LeererInhalt_LiefertLeerenString()
    {
        Assert.Equal(string.Empty, EmailTemplates.ToPlainText(string.Empty));
        Assert.Equal(string.Empty, EmailTemplates.ToPlainText("   "));
    }

    [Fact]
    public void ToPlainText_ReduziertMehrfacheLeerzeilen()
    {
        var text = EmailTemplates.ToPlainText("<p>A</p><p></p><p></p><p>B</p>");

        Assert.DoesNotContain("\n\n\n", text);
    }
}
