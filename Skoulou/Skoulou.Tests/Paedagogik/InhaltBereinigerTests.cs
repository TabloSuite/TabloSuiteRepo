using Skoulou.WebApp.Services.Paedagogik;

namespace Skoulou.Tests.Paedagogik;

/// <summary>Bereinigung des Inhalts-HTML beim Speichern (Quill-Format, Formeln, nur interne Medien).</summary>
public class InhaltBereinigerTests
{
    private readonly InhaltBereiniger bereiniger = new();

    [Fact]
    public void Entfernt_Skripte_Ereignisse_und_externe_Bilder()
    {
        var id = Guid.NewGuid();
        var html = bereiniger.Bereinige($"<p onclick=\"x()\">Bonjour<script>alert(1)</script></p><img src=\"https://evil.example/t.png\"><img src=\"/medien/{id}\" onerror=\"x()\"><img src=\"data:image/png;base64,AAAA\">");
        Assert.DoesNotContain("script", html);
        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("onerror", html);
        Assert.DoesNotContain("evil.example", html);
        Assert.DoesNotContain("data:image", html);
        Assert.Contains($"/medien/{id}", html);
        Assert.Equal([id], InhaltBereiniger.MedienIds(html));
    }

    [Fact]
    public void Formeln_bleiben_als_leerer_Span_mit_LaTeX()
    {
        var html = bereiniger.Bereinige("<p><span class=\"ql-formula\" data-value=\"\\frac{a}{b}\"><span contenteditable=\"false\"><span class=\"katex\">a/b</span></span></span></p>");
        Assert.Contains("class=\"ql-formula\"", html);
        Assert.Contains("data-value=\"\\frac{a}{b}\"", html);
        Assert.DoesNotContain("katex", html);
        Assert.DoesNotContain("contenteditable", html);

        var zuLang = bereiniger.Bereinige($"<p>x<span class=\"ql-formula\" data-value=\"{new string('x', 1001)}\">y</span></p>");
        Assert.DoesNotContain("data-value", zuLang);
    }

    [Fact]
    public void Quill_Listen_und_Klassen_bleiben_fremde_Attribute_nicht()
    {
        var html = bereiniger.Bereinige("<ol><li data-list=\"bullet\" data-x=\"1\" class=\"ql-indent-1 boese\">Un</li></ol><p class=\"ql-align-center\" style=\"color: red; position: fixed\">Centre</p><a href=\"javascript:alert(1)\">x</a>");
        Assert.Contains("data-list=\"bullet\"", html);
        Assert.DoesNotContain("data-x", html);
        Assert.DoesNotContain("boese", html);
        Assert.Contains("ql-indent-1", html);
        Assert.Contains("ql-align-center", html);
        Assert.DoesNotContain("position", html);
        Assert.DoesNotContain("javascript", html);
    }

    [Fact]
    public void Leere_Absaetze_sind_leer_und_Kurztext_ohne_HTML()
    {
        Assert.Equal(string.Empty, bereiniger.Bereinige("<p><br></p><p> </p>"));
        Assert.Equal(string.Empty, bereiniger.Bereinige(null));
        Assert.Equal("Le chat dort", InhaltBereiniger.Kurztext("<p>Le <b>chat</b></p><p>dort</p>"));
        Assert.EndsWith("…", InhaltBereiniger.Kurztext(new string('a', 300), 20));
    }
}
