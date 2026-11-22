using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Services.Noten;
using Skoulou.WebApp.Services.Organisation.Druck;

namespace Skoulou.Tests.Core;

/// <summary>
/// Klicktest 22.11.2026: In allen PDFs (Schrift Lato) kam « ti » beim Kopieren als U+099E (« Mathémaঞques ») und « fi/ffi » als
/// Ligaturzeichen (« Koﬃ ») heraus. Die Textebene (ToUnicode) darf nur echte Buchstaben enthalten → Standard-Ligaturen aus.
/// </summary>
public class PdfLigaturTests
{
    private const string Probe = "Mathématiques Inscription Koffi officiel fleur affiche";

    [Fact]
    public void Gemeinsame_Druckseite_hat_keine_Ligaturen_in_der_Textebene()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var pdf = Document.Create(doc => doc.Page(page =>
        {
            DruckStil.Seite(page, PageSizes.A4, 12);
            page.Content().Column(c =>
            {
                c.Item().Text(Probe);
                c.Item().DefaultTextStyle(s => s.FontSize(8)).Text(Probe);   // verschachtelter Stil erbt die Einstellung
                c.Item().Text(t => t.Span(Probe).Bold());
            });
        })).GeneratePdf();
        PruefeTextebene(pdf);
    }

    [Fact]
    public void Bulletin_hat_keine_Ligaturen_in_der_Textebene()
    {
        var inhalt = VorlagenService.Beispiel(new Schule { Name = "Collège Les Palmiers", Zeitzone = "Africa/Lome" },
            new BulletinVorlage { ZeigeTeilnoten = true, ZeigeFachRang = true, ZeigeNoteInWorten = true });
        var pdf = BulletinPdf.Erzeuge([new BulletinSeite(new BulletinSnapshot(1, inhalt,
            new SnapshotDokument("BUL-2026-00001", "19/12/2026", null, null, false, null, false)), null, null, BulletinStatus.Publie, null)]);
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
