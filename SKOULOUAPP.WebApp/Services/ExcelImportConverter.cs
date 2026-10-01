using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace HalloApp.WebApp.Services;

public static class ExcelImportConverter
{
    private const int MaxExpandedBytes = 8 * 1024 * 1024;
    private const int MaxRows = 10_000;
    private const int MaxColumns = 32;
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationship = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static string ConvertToCsv(Stream input)
    {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > 100 || archive.Entries.Sum(entry => entry.Length) > MaxExpandedBytes)
            throw new InvalidDataException("Le fichier Excel est trop volumineux.");
        var workbook = ReadXml(archive, "xl/workbook.xml");
        var sheet = workbook.Descendants(Main + "sheet").FirstOrDefault()
            ?? throw new InvalidDataException("Le classeur ne contient aucune feuille.");
        var relationId = (string?)sheet.Attribute(Relationship + "id")
            ?? throw new InvalidDataException("La première feuille est introuvable.");
        var relations = ReadXml(archive, "xl/_rels/workbook.xml.rels");
        var relation = relations.Descendants(PackageRelationship + "Relationship")
            .FirstOrDefault(x => (string?)x.Attribute("Id") == relationId);
        var target = (string?)relation?.Attribute("Target");
        if (string.IsNullOrWhiteSpace(target) || (string?)relation?.Attribute("TargetMode") == "External")
            throw new InvalidDataException("La première feuille est introuvable.");

        var path = target.TrimStart('/');
        if (!path.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) path = "xl/" + path;
        var segments = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment == "..") { if (segments.Count == 0) throw new InvalidDataException("Chemin de feuille invalide."); segments.RemoveAt(segments.Count - 1); }
            else if (segment.Length > 0 && segment != ".") segments.Add(segment);
        }
        path = string.Join('/', segments);
        if (!path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Chemin de feuille invalide.");

        var shared = archive.GetEntry("xl/sharedStrings.xml") is null
            ? []
            : ReadXml(archive, "xl/sharedStrings.xml").Descendants(Main + "si")
                .Select(x => string.Concat(x.Descendants(Main + "t").Select(t => t.Value))).ToList();
        var document = ReadXml(archive, path);
        var output = new StringBuilder();
        var rows = document.Descendants(Main + "sheetData").Elements(Main + "row").ToList();
        if (rows.Count > MaxRows) throw new InvalidDataException("La feuille contient trop de lignes.");
        foreach (var row in rows)
        {
            var fields = new string[MaxColumns];
            var lastColumn = -1;
            foreach (var cell in row.Elements(Main + "c"))
            {
                var reference = (string?)cell.Attribute("r") ?? string.Empty;
                var column = 0;
                foreach (var ch in reference)
                {
                    if (ch is >= 'A' and <= 'Z') column = checked(column * 26 + ch - 'A' + 1);
                    else if (ch is >= 'a' and <= 'z') column = checked(column * 26 + ch - 'a' + 1);
                    else break;
                }
                if (column is < 1 or > MaxColumns) throw new InvalidDataException("La feuille contient trop de colonnes.");
                var value = (string?)cell.Element(Main + "v") ?? string.Concat(cell.Descendants(Main + "is").Descendants(Main + "t").Select(t => t.Value));
                if ((string?)cell.Attribute("t") == "s")
                {
                    if (!int.TryParse(value, out var index) || index < 0 || index >= shared.Count)
                        throw new InvalidDataException("Référence de texte Excel invalide.");
                    value = shared[index];
                }
                fields[column - 1] = value;
                lastColumn = Math.Max(lastColumn, column - 1);
            }
            if (lastColumn < 0) continue;
            output.AppendJoin(';', fields.Take(lastColumn + 1).Select(EscapeCsv));
            output.Append('\n');
            if (output.Length > MaxExpandedBytes) throw new InvalidDataException("La feuille est trop volumineuse.");
        }
        return output.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        value = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static XDocument ReadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException("Fichier Excel incomplet.");
        if (entry.Length > MaxExpandedBytes) throw new InvalidDataException("Le fichier Excel est trop volumineux.");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxExpandedBytes,
            MaxCharactersFromEntities = 0
        });
        return XDocument.Load(reader);
    }
}
