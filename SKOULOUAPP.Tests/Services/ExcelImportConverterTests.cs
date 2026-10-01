using System.IO.Compression;
using System.Text;
using HalloApp.WebApp.Services;
using Xunit;

namespace HalloApp.Tests.Services;

public class ExcelImportConverterTests
{
    [Fact]
    public void ConvertToCsv_LiestGeteilteTexteUndErsteTabelle()
    {
        using var stream = CreateWorkbook(
            "<sst xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><si><t>Prénom</t></si><si><t>Nom</t></si><si><t>Classe</t></si><si><t>Afi</t></si><si><t>Mensah</t></si><si><t>CM1A</t></si></sst>",
            "<sheetData xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><row r='1'><c r='A1' t='s'><v>0</v></c><c r='B1' t='s'><v>1</v></c><c r='C1' t='s'><v>2</v></c></row><row r='2'><c r='A2' t='s'><v>3</v></c><c r='B2' t='s'><v>4</v></c><c r='C2' t='s'><v>5</v></c></row></sheetData>");

        Assert.Equal("Prénom;Nom;Classe\nAfi;Mensah;CM1A\n", ExcelImportConverter.ConvertToCsv(stream));
    }

    [Fact]
    public void ConvertToCsv_EscapedTextUndLeereSpalteBleibenErhalten()
    {
        using var stream = CreateWorkbook(null,
            "<sheetData xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><row r='1'><c r='A1' t='inlineStr'><is><t>Nom</t></is></c><c r='C1' t='inlineStr'><is><t>Classe</t></is></c></row><row r='2'><c r='A2' t='inlineStr'><is><t>Jean; \"Paul\"</t></is></c><c r='C2' t='inlineStr'><is><t>CP1A</t></is></c></row></sheetData>");

        Assert.Equal("Nom;;Classe\n\"Jean; \"\"Paul\"\"\";;CP1A\n", ExcelImportConverter.ConvertToCsv(stream));
    }

    [Fact]
    public void ConvertToCsv_LehntArchivMitZuGrosserEntpackterGroesseAb()
    {
        using var stream = CreateWorkbook(null, "<sheetData xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'/>",
            extra: new string('a', 8 * 1024 * 1024 + 1));
        Assert.Throws<InvalidDataException>(() => ExcelImportConverter.ConvertToCsv(stream));
    }

    private static MemoryStream CreateWorkbook(string? shared, string sheetData, string? extra = null)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "xl/workbook.xml", "<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Données' sheetId='1' r:id='rId1'/></sheets></workbook>");
            Write(zip, "xl/_rels/workbook.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Target='worksheets/sheet1.xml'/></Relationships>");
            Write(zip, "xl/worksheets/sheet1.xml", $"<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'>{sheetData}</worksheet>");
            if (shared is not null) Write(zip, "xl/sharedStrings.xml", shared);
            if (extra is not null) Write(zip, "extra.txt", extra);
        }
        stream.Position = 0;
        return stream;
    }

    private static void Write(ZipArchive zip, string path, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open(), Encoding.UTF8);
        writer.Write(content);
    }
}
