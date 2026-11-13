using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Skoulou.Core.Werkzeuge;

namespace Skoulou.Core.Paedagogik;

/// <summary>
/// Toleranter Textvergleich für Antworten (Bauplan §5.11): Groß/Klein egal, Akzente optional,
/// Leerzeichen und typografische Zeichen vereinheitlicht, Satzzeichen am Satz egal.
/// Bedeutungstragende Zeichen bleiben: Dezimalzahlen (« 3,5 » = « 3.5 », aber ≠ « 35 »),
/// Rechenzeichen und Klammern (« x = 2 » = « x=2 »), Apostrophe (« l’école » = « l'école »).
/// </summary>
public static partial class AntwortVergleich
{
    private const char DezimalPlatzhalter = '\u0001';

    /// <summary>Vergleichsform einer Antwort oder Musterlösung (leer, wenn nichts Sinnvolles bleibt).</summary>
    public static string Normalisiere(string? text, bool akzenteBeachten = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var s = OhneHtml(text).Normalize(NormalizationForm.FormC);

        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s) Vereinheitliche(c, sb);
        s = sb.ToString().ToLowerInvariant();
        if (!akzenteBeachten) s = TextNormalisierung.OhneAkzente(s);

        // Dezimaltrenner zwischen Ziffern schützen, danach Satzzeichen entfernen.
        s = DezimalTrenner().Replace(s, DezimalPlatzhalter.ToString());
        sb.Clear();
        foreach (var c in s) sb.Append(IstSatzzeichen(c) ? ' ' : c);
        s = sb.ToString().Replace(DezimalPlatzhalter, ',');

        s = Leerraum().Replace(s, " ").Trim();
        s = UmOperatoren().Replace(s, "$1");
        return s;
    }

    /// <summary>Passt die Antwort zu einer der Varianten? Leere Antworten passen nie.</summary>
    public static bool Passt(string? antwort, IEnumerable<string?> varianten, bool akzenteBeachten = false)
    {
        var a = Normalisiere(antwort, akzenteBeachten);
        if (a.Length == 0) return false;
        foreach (var v in varianten)
        {
            var n = Normalisiere(v, akzenteBeachten);
            if (n.Length > 0 && n == a) return true;
        }
        return false;
    }

    /// <summary>Entfernt HTML (Blockenden werden Leerzeichen) und dekodiert Entities.</summary>
    public static string OhneHtml(string text)
    {
        if (text.Contains('<'))
        {
            text = Blockende().Replace(text, " ");
            text = Tag().Replace(text, string.Empty);
        }
        return text.Contains('&') ? WebUtility.HtmlDecode(text) : text;
    }

    private static void Vereinheitliche(char c, StringBuilder sb)
    {
        switch (c)
        {
            case '’' or '‘' or '‛' or 'ʼ' or '´' or '`' or '′' or '＇':
                sb.Append('\''); break;
            case '«' or '»' or '“' or '”' or '„' or '‟' or '"' or '‹' or '›':
                sb.Append(' '); break;
            case '–' or '—' or '‒' or '−' or '‐' or '‑':
                sb.Append('-'); break;
            case '…':
                sb.Append("..."); break;
            case 'œ': sb.Append("oe"); break;
            case 'Œ': sb.Append("OE"); break;
            case 'æ': sb.Append("ae"); break;
            case 'Æ': sb.Append("AE"); break;
            case '​' or '‌' or '‍' or '﻿':
                break;
            default:
                sb.Append(char.IsWhiteSpace(c) ? ' ' : c);
                break;
        }
    }

    /// <summary>Satzzeichen ohne Bedeutung für die Antwort (Apostroph, Rechenzeichen, Klammern zählen nicht dazu).</summary>
    private static bool IstSatzzeichen(char c) => c is '.' or ',' or ';' or ':' or '!' or '?' or '¡' or '¿';

    [GeneratedRegex(@"(?<=\d)[.,](?=\d)")]
    private static partial Regex DezimalTrenner();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Leerraum();

    [GeneratedRegex(@"\s*([-+*/=<>^()'\[\]{}])\s*")]
    private static partial Regex UmOperatoren();

    [GeneratedRegex(@"<\s*br\s*/?\s*>|</\s*(p|div|li|h[1-6]|tr|td|th|blockquote)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex Blockende();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tag();
}
