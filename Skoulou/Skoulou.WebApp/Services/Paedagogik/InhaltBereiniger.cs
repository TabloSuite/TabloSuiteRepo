using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>
/// Bereinigt Lerninhalte (Quill-HTML) beim Speichern: Grundregeln wie <see cref="HtmlBereiniger"/>
/// (keine Skripte, Bilder nur aus « /medien/… »), dazu erlaubt: Quill-Klassen (« ql-… »), Listen
/// (<c>data-list</c>) und Formeln (<c>span.ql-formula[data-value]</c>, ohne gerenderten KaTeX-Inhalt –
/// gerendert wird beim Anzeigen).
/// </summary>
public sealed partial class InhaltBereiniger
{
    public const int MaxFormelLaenge = 1000;

    private readonly HtmlSanitizer sanitizer;
    private readonly HtmlParser parser = new();

    public InhaltBereiniger()
    {
        sanitizer = new HtmlSanitizer();
        sanitizer.AllowedAttributes.Add("class");
        sanitizer.AllowDataAttributes = true;
        sanitizer.AllowedSchemes.Add("mailto");
        sanitizer.AllowedCssProperties.Clear();
        foreach (var p in new[] { "color", "background-color", "text-align" }) sanitizer.AllowedCssProperties.Add(p);
        sanitizer.FilterUrl += (_, e) =>
        {
            if (string.Equals(e.Tag.TagName, "IMG", StringComparison.OrdinalIgnoreCase) && !HtmlBereiniger.IstIntern(e.OriginalUrl))
                e.SanitizedUrl = null;
        };
    }

    /// <summary>Bereinigtes HTML (leer bei leerer Eingabe oder nur leeren Absätzen).</summary>
    public string Bereinige(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var sauber = sanitizer.Sanitize(html);
        var dokument = parser.ParseDocument("<!DOCTYPE html><html><body>" + sauber + "</body></html>");
        var body = dokument.Body!;
        foreach (var el in body.QuerySelectorAll("*").ToList()) Nachbearbeiten(el);
        // Bilder ohne gültige Quelle entfernen (der Sanitizer lässt <img> ohne src stehen).
        foreach (var img in body.QuerySelectorAll("img").Where(i => string.IsNullOrEmpty(i.GetAttribute("src"))).ToList()) img.Remove();
        var ergebnis = body.InnerHtml.Trim();
        return IstLeer(ergebnis) ? string.Empty : ergebnis;
    }

    private static void Nachbearbeiten(IElement el)
    {
        var istFormel = el.LocalName == "span" && el.ClassList.Contains("ql-formula");
        foreach (var name in el.Attributes.Select(a => a.Name).ToList())
        {
            if (name == "contenteditable" || name == "spellcheck") el.RemoveAttribute(name);
            else if (name.StartsWith("data-", StringComparison.Ordinal)
                     && !(name == "data-list" && el.LocalName == "li")
                     && !(name == "data-value" && istFormel))
                el.RemoveAttribute(name);
        }

        var fremd = el.ClassList.Where(k => !k.StartsWith("ql-", StringComparison.Ordinal)).ToList();
        foreach (var k in fremd) el.ClassList.Remove(k);
        if (el.ClassList.Length == 0) el.RemoveAttribute("class");

        if (istFormel)
        {
            var wert = Steuerzeichen().Replace(el.GetAttribute("data-value") ?? string.Empty, string.Empty).Trim();
            if (wert.Length is 0 or > MaxFormelLaenge)
            {
                el.TextContent = string.Empty;
                el.RemoveAttribute("data-value");
            }
            else
            {
                el.SetAttribute("data-value", wert);
                el.TextContent = string.Empty;
            }
        }
        else if (el.LocalName == "a")
        {
            el.SetAttribute("target", "_blank");
            el.SetAttribute("rel", "noopener noreferrer");
        }
        else if (el.LocalName == "span" && el.ClassList.Contains("ql-ui"))
        {
            el.TextContent = string.Empty;
        }
    }

    /// <summary>Nur leere Absätze (wie Quill sie für « leer » liefert)?</summary>
    private static bool IstLeer(string html)
        => html.Length == 0 || (LeererAbsatz().Replace(html, string.Empty).Trim().Length == 0 && !html.Contains("<img", StringComparison.OrdinalIgnoreCase)
                                && !html.Contains("ql-formula", StringComparison.Ordinal));

    /// <summary>Alle internen Medien (/medien/{id}), auf die das HTML verweist.</summary>
    public static IReadOnlyList<Guid> MedienIds(string? html)
    {
        if (string.IsNullOrEmpty(html)) return [];
        return MedienVerweis().Matches(html).Select(m => Guid.TryParse(m.Groups[1].Value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty).Distinct().ToList();
    }

    /// <summary>Kurzer Klartext (Listen, Korrektur).</summary>
    public static string Kurztext(string? html, int laenge = 140)
    {
        var text = Core.Paedagogik.AntwortVergleich.OhneHtml(html ?? string.Empty);
        text = Leerraum().Replace(text, " ").Trim();
        return text.Length <= laenge ? text : text[..laenge].TrimEnd() + "…";
    }

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F]")]
    private static partial Regex Steuerzeichen();

    [GeneratedRegex(@"<p>(\s|<br\s*/?>|&nbsp;)*</p>", RegexOptions.IgnoreCase)]
    private static partial Regex LeererAbsatz();

    [GeneratedRegex(@"/medien/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})")]
    private static partial Regex MedienVerweis();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Leerraum();
}
