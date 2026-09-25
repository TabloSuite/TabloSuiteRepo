using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace HalloApp.Core.Services;

/// <summary>
/// Zentrale Hilfsklasse für den toleranten Vergleich von Freitext-/Lücken-/Satzbau-Antworten.
/// Vergleich ignoriert Groß-/Kleinschreibung, führende/folgende sowie mehrfache Leerzeichen,
/// Satzzeichen und diakritische Zeichen. Mehrere akzeptierte Varianten pro Antwort werden unterstützt.
/// </summary>
public static class AnswerMatchingService
{
    /// <summary>
    /// Normalisiert einen Text für den toleranten Vergleich.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalizedCase = StripHtml(value).Trim();

        // Diakritische Zeichen entfernen (é -> e), aber deutsche Umlaute/ß erhalten bleiben sinnvoll.
        var sb = new StringBuilder(normalizedCase.Length);
        var lastWasSpace = false;
        foreach (var ch in normalizedCase)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            // Satzzeichen werden ignoriert (übersprungen).
        }

        var normalized = sb.ToString().TrimEnd();
        return RemoveDiacritics(normalized);
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string StripHtml(string value)
    {
        var withoutBlocks = Regex.Replace(value, "</(p|div|li|h[1-6]|br)>", " ", RegexOptions.IgnoreCase);
        var withoutTags = Regex.Replace(withoutBlocks, "<[^>]+>", " ");
        return WebUtility.HtmlDecode(withoutTags);
    }

    /// <summary>
    /// Prüft, ob die Schülerantwort mit mindestens einer akzeptierten Variante übereinstimmt.
    /// </summary>
    public static bool Matches(string? studentAnswer, IEnumerable<string> acceptedVariants)
    {
        var normalizedStudent = Normalize(studentAnswer);
        if (normalizedStudent.Length == 0)
        {
            return false;
        }

        foreach (var variant in acceptedVariants)
        {
            if (Normalize(variant) == normalizedStudent)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Deserialisiert ein JSON-Array akzeptierter Varianten (z. B. ["bin","heisse"]).
    /// Liefert eine leere Liste bei null/leer/ungültigem JSON.
    /// </summary>
    public static IReadOnlyList<string> ParseAcceptedAnswers(string? acceptedAnswersJson)
    {
        if (string.IsNullOrWhiteSpace(acceptedAnswersJson))
        {
            return [];
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(acceptedAnswersJson);
            return list?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Deserialisiert die Lücken-Definition: ein Array pro Lücke mit den jeweils akzeptierten Varianten,
    /// z. B. [["bin","heisse"],["bin"]]. Liefert eine leere Liste bei null/leer/ungültigem JSON.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> ParseGapAnswers(string? gapAnswersJson)
    {
        if (string.IsNullOrWhiteSpace(gapAnswersJson))
        {
            return [];
        }

        try
        {
            var gaps = JsonSerializer.Deserialize<List<List<string>>>(gapAnswersJson);
            if (gaps is null)
            {
                return [];
            }

            return gaps
                .Select(g => (IReadOnlyList<string>)(g?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? []))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Deserialisiert die vom Schüler eingegebenen Lückenwerte (ein JSON-String-Array).
    /// </summary>
    public static IReadOnlyList<string> ParseStudentGapValues(string? gapValuesJson)
    {
        if (string.IsNullOrWhiteSpace(gapValuesJson))
        {
            return [];
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(gapValuesJson);
            return list?.Select(x => x ?? string.Empty).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Bewertet eine Lückentext-Antwort. Liefert die Anzahl korrekt gefüllter Lücken und die Gesamtzahl.
    /// </summary>
    public static (int correct, int total) GradeGaps(string? gapDefinitionJson, string? studentGapValuesJson)
    {
        var definition = ParseGapAnswers(gapDefinitionJson);
        var studentValues = ParseStudentGapValues(studentGapValuesJson);

        var total = definition.Count;
        var correct = 0;
        for (var i = 0; i < definition.Count; i++)
        {
            var studentValue = i < studentValues.Count ? studentValues[i] : null;
            if (Matches(studentValue, definition[i]))
            {
                correct++;
            }
        }

        return (correct, total);
    }

    /// <summary>
    /// Zerlegt einen mehrzeiligen Text (eine Variante pro Zeile) in eine Liste getrimmter, nicht-leerer Varianten.
    /// </summary>
    public static List<string> SplitVariants(string? multiLine)
    {
        if (string.IsNullOrWhiteSpace(multiLine))
        {
            return [];
        }

        // RichText-Editor liefert HTML. Für Varianten behalten wir Zeilen-/Blocktrennungen
        // bei (p/div/li/br), damit "eine Variante pro Zeile" weiterhin korrekt funktioniert.
        var normalized = multiLine
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</div>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</li>", "\n", StringComparison.OrdinalIgnoreCase);

        var plainText = Regex.Replace(normalized, "<[^>]+>", " ");
        plainText = WebUtility.HtmlDecode(plainText);

        return plainText
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Serialisiert eine Variantenliste als JSON-Array. Liefert null, wenn keine Varianten vorhanden sind.
    /// </summary>
    public static string? SerializeVariants(IEnumerable<string> variants)
    {
        var list = variants.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
        return list.Count == 0 ? null : JsonSerializer.Serialize(list);
    }

    /// <summary>
    /// Serialisiert die Lücken-Definition (Liste von Variantenlisten) als JSON. Liefert null bei keiner Lücke.
    /// </summary>
    public static string? SerializeGaps(IEnumerable<IEnumerable<string>> gaps)
    {
        var list = gaps
            .Select(g => g.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList())
            .ToList();
        return list.Count == 0 ? null : JsonSerializer.Serialize(list);
    }

    /// <summary>
    /// Serialisiert Schüler-Lückenwerte als JSON-Array.
    /// </summary>
    public static string? SerializeStudentGapValues(IEnumerable<string> values)
    {
        var list = values.Select(x => x ?? string.Empty).ToList();
        return list.Count == 0 ? null : JsonSerializer.Serialize(list);
    }
}
