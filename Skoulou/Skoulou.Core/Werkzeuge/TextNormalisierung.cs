using System.Globalization;
using System.Text;

namespace Skoulou.Core.Werkzeuge;

/// <summary>Hilfen für Vergleiche ohne Akzente, Groß-/Kleinschreibung und Mehrfach-Leerzeichen.</summary>
public static class TextNormalisierung
{
    /// <summary>Entfernt diakritische Zeichen (« Éléonore » → « Eleonore »).</summary>
    public static string OhneAkzente(string text)
    {
        var zerlegt = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(zerlegt.Length);
        foreach (var c in zerlegt)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Vergleichsschlüssel: klein, ohne Akzente, Satzzeichen zu Leerzeichen, einfache Leerzeichen.</summary>
    public static string Schluessel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var basis = OhneAkzente(text.Trim()).ToLowerInvariant();
        var sb = new StringBuilder(basis.Length);
        var letztesLeer = false;
        foreach (var c in basis)
        {
            var istText = char.IsLetterOrDigit(c);
            if (istText) { sb.Append(c); letztesLeer = false; }
            else if (!letztesLeer && sb.Length > 0) { sb.Append(' '); letztesLeer = true; }
        }
        return sb.ToString().TrimEnd();
    }
}
