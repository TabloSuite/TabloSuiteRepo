using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ENote.WebApp.Services;
using QuestPDF.Fluent;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Klicktest 22.11.2026 (Befund aus SKOULOU, gleiche Ursache in eNOTE): Die Standardschrift der PDFs bildete Ligaturen, deren
/// Textebene falsch ist (« Bénéﬁce », Fremdzeichen statt « ti »). Étiquettes und Grundstil dürfen nur echte Buchstaben in der
/// Textebene (ToUnicode) haben.
/// </summary>
public class PdfLigaturTests
{
    private const string Probe = "Bénéfice officiel Coiffure Prestations affiche";

    [Fact]
    public void Etiketten_haben_keine_Ligaturen_in_der_Textebene()
        => PruefeTextebene(EtikettPdfService.ErzeugeA4Bogen([new EtikettPdfService.Etikett("ABC123", Probe, "1.500 F")]));

    [Fact]
    public void Grundstil_schaltet_Ligaturen_ab()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var pdf = QuestPDF.Fluent.Document.Create(doc => doc.Page(page =>
        {
            page.DefaultTextStyle(x => PdfTextStil.OhneLigaturen(x).FontSize(10));
            page.Content().Text(Probe);
        })).GeneratePdf();
        PruefeTextebene(pdf);
    }

    private static void PruefeTextebene(byte[] pdf)
    {
        var ziele = ToUnicodeZiele(pdf);
        Assert.NotEmpty(ziele);
        var falsch = ziele.Where(z => z is 0 or 0x099E or 0x08BC || (z >= 0xFB00 && z <= 0xFB06)).Select(z => $"U+{z:X4}").Distinct().ToList();
        Assert.True(falsch.Count == 0, "Ligaturzeichen in der Textebene: " + string.Join(", ", falsch));
    }

    /// <summary>Alle Unicode-Zielwerte aus den ToUnicode-Tabellen (bfchar/bfrange) der komprimierten Streams.</summary>
    private static List<int> ToUnicodeZiele(byte[] pdf)
    {
        var latin = Encoding.Latin1.GetString(pdf);
        var ziele = new List<int>();
        var pos = 0;
        while ((pos = latin.IndexOf("stream", pos, StringComparison.Ordinal)) >= 0)
        {
            if (pos >= 3 && latin.AsSpan(pos - 3, 3).SequenceEqual("end")) { pos += 6; continue; }
            var start = pos + 6;
            if (start < latin.Length && latin[start] == '\r') start++;
            if (start < latin.Length && latin[start] == '\n') start++;
            var ende = latin.IndexOf("endstream", start, StringComparison.Ordinal);
            if (ende < 0) break;
            pos = ende + 9;
            string text;
            try
            {
                using var ein = new ZLibStream(new MemoryStream(pdf, start, ende - start), CompressionMode.Decompress);
                using var aus = new MemoryStream();
                ein.CopyTo(aus);
                text = Encoding.Latin1.GetString(aus.ToArray());
            }
            catch (InvalidDataException) { continue; }
            if (!text.Contains("beginbf", StringComparison.Ordinal)) continue;
            foreach (Match m in Regex.Matches(text, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
                foreach (Match e in Regex.Matches(m.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                    ziele.AddRange(Codepunkte(e.Groups[2].Value));
            foreach (Match m in Regex.Matches(text, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
                foreach (Match e in Regex.Matches(m.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(<[0-9A-Fa-f]+>|\[[^\]]*\])"))
                {
                    var von = int.Parse(e.Groups[1].Value, NumberStyles.HexNumber);
                    var bis = int.Parse(e.Groups[2].Value, NumberStyles.HexNumber);
                    var ziel = e.Groups[3].Value;
                    if (ziel.StartsWith('['))
                        foreach (Match z in Regex.Matches(ziel, @"<([0-9A-Fa-f]+)>")) ziele.AddRange(Codepunkte(z.Groups[1].Value));
                    else
                    {
                        var basis = Codepunkte(ziel.Trim('<', '>'));
                        for (var i = 0; i <= bis - von && basis.Count > 0; i++) ziele.Add(basis[^1] + i);
                    }
                }
        }
        return ziele;
    }

    /// <summary>UTF-16BE-Hex in Codepunkte.</summary>
    private static List<int> Codepunkte(string hex)
    {
        var bytes = Convert.FromHexString(hex.Length % 4 == 0 ? hex : hex.PadLeft(hex.Length + 4 - hex.Length % 4, '0'));
        var s = Encoding.BigEndianUnicode.GetString(bytes);
        var liste = new List<int>();
        for (var i = 0; i < s.Length; i++)
        {
            liste.Add(char.ConvertToUtf32(s, i));
            if (char.IsHighSurrogate(s[i])) i++;
        }
        return liste;
    }
}
