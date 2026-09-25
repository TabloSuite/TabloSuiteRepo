using System.Text;
using System.Text.RegularExpressions;

namespace HalloApp.Core.Services.Text;

/// <summary>
/// Leichtgewichtiger, allowlist-basierter HTML-Sanitizer ohne externe Abhängigkeit.
/// Er ist auf die vom Quill-Editor erzeugte Formatierung ausgelegt und entfernt
/// nicht erlaubte Tags, Attribute sowie potenziell gefährliche Inhalte
/// (Skripte, Event-Handler, gefährliche URLs) zum Schutz vor XSS.
/// </summary>
public static partial class HtmlSanitizer
{
    private static readonly HashSet<string> ErlaubteTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "span", "div",
        "strong", "b", "em", "i", "u", "s", "strike",
        "ul", "ol", "li",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "blockquote", "pre", "code",
        "sub", "sup", "a"
    };

    // Zusätzliches Tag, das nur im Bild-Modus erlaubt ist.
    private const string BildTag = "img";

    private static readonly HashSet<string> ErlaubteAttribute = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "target", "rel", "class", "style", "data-value"
    };

    // Zusätzliche Attribute, die nur für <img> im Bild-Modus erlaubt sind.
    private static readonly HashSet<string> ErlaubteBildAttribute = new(StringComparer.OrdinalIgnoreCase)
    {
        "src", "alt", "width", "height", "class", "style"
    };

    private static readonly HashSet<string> ErlaubteStilEigenschaften = new(StringComparer.OrdinalIgnoreCase)
    {
        "color", "background-color", "text-align", "font-family", "font-size", "font-weight", "text-decoration"
    };

    [GeneratedRegex(@"<\s*(script|style|iframe|object|embed|link|meta|head|title|base|form|input|button|textarea|select)\b[^>]*>.*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex GefaehrlicheBloeckeRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex KommentarRegex();

    [GeneratedRegex(@"</?\s*([a-zA-Z][a-zA-Z0-9]*)\b([^>]*)>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"([a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))", RegexOptions.Singleline)]
    private static partial Regex AttributRegex();

    /// <summary>
    /// Bereinigt den übergebenen HTML-Text anhand der Allowlist. Gibt bei
    /// null/leer einen leeren String zurück.
    /// </summary>
    public static string Sanitize(string? html)
    {
        return SanitizeIntern(html, erlaubeBilder: false);
    }

    /// <summary>
    /// Wie <see cref="Sanitize"/>, erlaubt zusätzlich eingebettete Bilder
    /// (&lt;img&gt; mit data:- oder https:-URLs). Gedacht für Rich-Text-Inhalte,
    /// in denen Bilder eingefügt werden dürfen (z. B. Aufgaben-Templates,
    /// Lektionsseiten).
    /// </summary>
    public static string SanitizeMitBildern(string? html)
    {
        return SanitizeIntern(html, erlaubeBilder: true);
    }

    private static string SanitizeIntern(string? html, bool erlaubeBilder)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        // Gefährliche Blöcke inklusive Inhalt entfernen.
        var bereinigt = GefaehrlicheBloeckeRegex().Replace(html, string.Empty);
        bereinigt = KommentarRegex().Replace(bereinigt, string.Empty);

        // Alle verbleibenden Tags durchgehen und filtern; Text bleibt erhalten.
        var ergebnis = TagRegex().Replace(bereinigt, match => VerarbeiteTag(match, erlaubeBilder));

        // Übrig gebliebene, nicht erlaubte "javascript:"-artige Reste neutralisieren.
        return ergebnis.Trim();
    }

    /// <summary>
    /// Wandelt HTML in reinen Text um: entfernt alle Tags, dekodiert einfache HTML-Entities
    /// und normalisiert Whitespace. Für tolerante Textvergleiche gedacht.
    /// </summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var decoded = System.Net.WebUtility.HtmlDecode(html);

        var text = GefaehrlicheBloeckeRegex().Replace(decoded, " ");
        text = KommentarRegex().Replace(text, " ");
        text = TagRegex().Replace(text, " ");

        text = text
            .Replace("&nbsp;", " ")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&apos;", "'");

        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string VerarbeiteTag(Match match, bool erlaubeBilder)
    {
        var tagName = match.Groups[1].Value.ToLowerInvariant();
        var istBild = erlaubeBilder && tagName == BildTag;
        if (!ErlaubteTags.Contains(tagName) && !istBild)
        {
            // Nicht erlaubtes Tag komplett entfernen (Inhalt bleibt, da nur der Tag ersetzt wird).
            return string.Empty;
        }

        var voll = match.Value;
        var istEndTag = voll.StartsWith("</", StringComparison.Ordinal)
            || Regex.IsMatch(voll, @"^<\s*/", RegexOptions.Singleline);

        if (istEndTag)
        {
            return $"</{tagName}>";
        }

        var istSelbstschliessend = voll.TrimEnd().EndsWith("/>", StringComparison.Ordinal);
        var attributTeil = match.Groups[2].Value;
        var attribute = BereinigeAttribute(tagName, attributTeil, istBild);

        var builder = new StringBuilder();
        builder.Append('<').Append(tagName);
        if (attribute.Length > 0)
        {
            builder.Append(' ').Append(attribute);
        }

        if (istSelbstschliessend || tagName == "br" || istBild)
        {
            builder.Append(" />");
        }
        else
        {
            builder.Append('>');
        }

        return builder.ToString();
    }

    private static string BereinigeAttribute(string tagName, string attributTeil, bool istBild)
    {
        if (string.IsNullOrWhiteSpace(attributTeil))
        {
            return string.Empty;
        }

        var erlaubteAttribute = istBild ? ErlaubteBildAttribute : ErlaubteAttribute;

        var teile = new List<string>();
        var hatHref = false;
        var hatRel = false;
        var hatTarget = false;
        foreach (Match attr in AttributRegex().Matches(attributTeil))
        {
            var name = attr.Groups[1].Value.ToLowerInvariant();
            if (!erlaubteAttribute.Contains(name) || name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var wert = attr.Groups[2].Success ? attr.Groups[2].Value
                : attr.Groups[3].Success ? attr.Groups[3].Value
                : attr.Groups[4].Value;

            var bereinigterWert = BereinigeAttributWert(tagName, name, wert, istBild);
            if (bereinigterWert is null)
            {
                continue;
            }

            if (name == "href")
            {
                hatHref = true;
            }
            else if (name == "rel")
            {
                hatRel = true;
            }
            else if (name == "target")
            {
                hatTarget = true;
            }

            teile.Add($"{name}=\"{bereinigterWert}\"");
        }

        // Für gültige Links immer target="_blank" und rel="noopener noreferrer" erzwingen.
        if (tagName == "a" && hatHref && !hatRel)
        {
            teile.Add("rel=\"noopener noreferrer\"");
        }
        if (tagName == "a" && hatHref && !hatTarget)
        {
            teile.Add("target=\"_blank\"");
        }

        return string.Join(' ', teile);
    }

    private static string? BereinigeAttributWert(string tagName, string name, string wert, bool istBild)
    {
        var normalisiert = System.Net.WebUtility.HtmlDecode(wert).Trim();

        switch (name)
        {
            case "src":
                if (!istBild || tagName != "img")
                {
                    return null;
                }

                // Nur eingebettete Bild-Daten oder sichere https-URLs zulassen.
                if (normalisiert.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
                    || normalisiert.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return System.Net.WebUtility.HtmlEncode(normalisiert);
                }

                return null;

            case "alt":
                return istBild ? System.Net.WebUtility.HtmlEncode(normalisiert) : null;

            case "width":
            case "height":
                if (!istBild)
                {
                    return null;
                }

                // Nur reine Zahlen oder Prozent-/Pixelangaben zulassen.
                return Regex.IsMatch(normalisiert, @"^\d+(\.\d+)?(px|%)?$")
                    ? System.Net.WebUtility.HtmlEncode(normalisiert)
                    : null;

            case "href":
                if (tagName != "a")
                {
                    return null;
                }

                if (normalisiert.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || normalisiert.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || normalisiert.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    return System.Net.WebUtility.HtmlEncode(normalisiert);
                }

                return null;

            case "target":
                return normalisiert.Equals("_blank", StringComparison.OrdinalIgnoreCase) ? "_blank" : null;

            case "rel":
                return "noopener noreferrer";

            case "class":
                // Nur Quill-Formatklassen zulassen (z. B. ql-align-center, ql-font-serif).
                var klassen = normalisiert
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(k => k.StartsWith("ql-", StringComparison.OrdinalIgnoreCase)
                        && Regex.IsMatch(k, "^[a-zA-Z0-9_-]+$"))
                    .ToArray();
                return klassen.Length > 0 ? System.Net.WebUtility.HtmlEncode(string.Join(' ', klassen)) : null;

            case "data-value":
                // Quill speichert LaTeX-Formeln als <span class="ql-formula" data-value="…">.
                // Begrenzen und nur druckbare Zeichen erlauben, damit keine HTML-/Script-Nutzlast persistiert wird.
                if (tagName == "span" && normalisiert.Length <= 1_000 &&
                    normalisiert.All(character => !char.IsControl(character) && character != '<' && character != '>'))
                {
                    return System.Net.WebUtility.HtmlEncode(normalisiert);
                }

                return null;

            case "style":
                return BereinigeStil(normalisiert);

            default:
                return null;
        }
    }

    private static string? BereinigeStil(string stil)
    {
        var deklarationen = new List<string>();
        foreach (var teil in stil.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = teil.IndexOf(':');
            if (index <= 0)
            {
                continue;
            }

            var eigenschaft = teil[..index].Trim().ToLowerInvariant();
            var wert = teil[(index + 1)..].Trim();

            if (!ErlaubteStilEigenschaften.Contains(eigenschaft))
            {
                continue;
            }

            // Gefährliche Werte ausschließen.
            if (wert.Contains("url(", StringComparison.OrdinalIgnoreCase)
                || wert.Contains("expression", StringComparison.OrdinalIgnoreCase)
                || wert.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
                || wert.Contains('<') || wert.Contains('>') || wert.Contains('"'))
            {
                continue;
            }

            deklarationen.Add($"{eigenschaft}: {wert}");
        }

        if (deklarationen.Count == 0)
        {
            return null;
        }

        return System.Net.WebUtility.HtmlEncode(string.Join("; ", deklarationen));
    }
}
