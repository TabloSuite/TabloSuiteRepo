using Ganss.Xss;

namespace Skoulou.WebApp.Services.Infrastruktur;

/// <summary>
/// Bereinigt HTML beim Speichern (Ganss.Xss): keine Skripte, keine Ereignis-Attribute,
/// Bilder nur aus internen Medien (« /medien/… »), keine externen Bilder.
/// </summary>
public sealed class HtmlBereiniger
{
    private readonly HtmlSanitizer sanitizer;

    public HtmlBereiniger()
    {
        sanitizer = new HtmlSanitizer();
        sanitizer.AllowedSchemes.Add("mailto");
        sanitizer.AllowedSchemes.Add("tel");
        sanitizer.FilterUrl += (_, e) =>
        {
            if (string.Equals(e.Tag.TagName, "IMG", StringComparison.OrdinalIgnoreCase) && !IstIntern(e.OriginalUrl))
                e.SanitizedUrl = null;
        };
    }

    /// <summary>Interne Medien-URL (relativ, unter /medien/)?</summary>
    public static bool IstIntern(string? url)
        => !string.IsNullOrWhiteSpace(url) && url.StartsWith("/medien/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bereinigtes HTML (leer bei leerer Eingabe).</summary>
    public string Bereinige(string? html) => string.IsNullOrWhiteSpace(html) ? string.Empty : sanitizer.Sanitize(html);

    /// <summary>Klartext als sicheres HTML (Zeilenumbrüche als &lt;br&gt;).</summary>
    public static string TextAlsHtml(string? text)
        => System.Net.WebUtility.HtmlEncode(text ?? string.Empty).Replace("\r\n", "\n").Replace("\n", "<br>");
}
