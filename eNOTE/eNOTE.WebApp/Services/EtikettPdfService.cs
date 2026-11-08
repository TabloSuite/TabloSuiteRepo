using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ZXing;
using ZXing.Common;

namespace ENote.WebApp.Services;

/// <summary>
/// Erzeugt Barcode-Etiketten als PDF: A4-Bogen mit Raster (für jeden Drucker +
/// Etikettenpapier) oder Einzel-Etiketten 62×29 mm (für Thermo-Etikettendrucker).
/// </summary>
public static class EtikettPdfService
{
    /// <summary>Eine Etikette: Code + Anzeige-Texte.</summary>
    public record Etikett(string Code, string Name, string? PreisText);

    /// <summary>A4-Bogen mit Etiketten-Raster (3 Spalten × 8 Reihen, je 62×29 mm).</summary>
    public static byte[] ErzeugeA4Bogen(IReadOnlyList<Etikett> etiketten)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var dokument = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0.7f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(8));

                page.Content().Table(table =>
                {
                    const int spalten = 3;
                    table.ColumnsDefinition(cols =>
                    {
                        for (var i = 0; i < spalten; i++)
                        {
                            cols.RelativeColumn();
                        }
                    });

                    for (var i = 0; i < etiketten.Count; i += spalten)
                    {
                        for (var spalte = 0; spalte < spalten; spalte++)
                        {
                            var index = i + spalte;
                            if (index < etiketten.Count)
                            {
                                var etikett = etiketten[index];
                                table.Cell().Element(c => EtikettZelle(c, etikett));
                            }
                            else
                            {
                                table.Cell().Element(c => c.Height(29, Unit.Millimetre));
                            }
                        }
                    }
                });
            });
        });

        return dokument.GeneratePdf();
    }

    /// <summary>Einzel-Etikett 62×29 mm (für Thermo-Etikettendrucker).</summary>
    public static byte[] ErzeugeEinzelEtikett(Etikett etikett)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var dokument = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(62, 29, Unit.Millimetre);
                page.Margin(1, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontSize(7));
                page.Content().Width(60, Unit.Millimetre).Height(27, Unit.Millimetre)
                    .Element(c => EtikettZelle(c, etikett));
            });
        });

        return dokument.GeneratePdf();
    }

    private static void EtikettZelle(IContainer container, Etikett etikett)
    {
        var barcode = ErzeugeCode128(etikett.Code);

        container
            .Border(0.5f)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(2)
            .Column(col =>
            {
                col.Item().AlignCenter().Text(etikett.Name).FontSize(7).Bold();
                if (!string.IsNullOrWhiteSpace(etikett.PreisText))
                {
                    col.Item().AlignCenter().Text(etikett.PreisText).FontSize(6.5f).FontColor(Colors.Grey.Darken1);
                }

                if (barcode is not null)
                {
                    col.Item().AlignCenter().MaxHeight(12, Unit.Millimetre).Svg(barcode).FitArea();
                }

                col.Item().AlignCenter().Text(etikett.Code).FontSize(5.5f).FontColor(Colors.Grey.Darken1);
            });
    }

    /// <summary>Erzeugt einen Code-128-Barcode als plattformunabhängige SVG-Grafik.</summary>
    internal static string? ErzeugeCode128(string inhalt)
    {
        if (string.IsNullOrWhiteSpace(inhalt))
        {
            return null;
        }

        try
        {
            var writer = new BarcodeWriterSvg
            {
                Format = BarcodeFormat.CODE_128,
                Options = new EncodingOptions { Height = 60, Width = 300, Margin = 2, PureBarcode = true }
            };
            return writer.Write(inhalt).Content;
        }
        catch
        {
            return null; // Barcode darf das PDF nie verhindern.
        }
    }
}
