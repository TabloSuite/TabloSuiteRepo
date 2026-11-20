using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ZXing;
using ZXing.Common;

namespace ENote.WebApp.Services;

/// <summary>
/// Erzeugt Barcode-Etiketten als PDF: A4-Etikettenbogen 70×37 mm (24 je Bogen, für jeden Drucker)
/// oder Einzel-Etiketten 62×29 mm (für Thermo-Etikettendrucker).
/// </summary>
public static class EtikettPdfService
{
    /// <summary>Eine Etikette: Code + Anzeige-Texte.</summary>
    public record Etikett(string Code, string Name, string? PreisText);

    /// <summary>Handelsüblicher A4-Bogen (Nutzer 20.11.2026): 3 Spalten × 8 Reihen, je 70×37 mm, ohne Rand – 24 Etiketten.</summary>
    public const int BogenSpalten = 3, BogenReihen = 8, BogenEtiketten = BogenSpalten * BogenReihen;
    public const float BogenBreiteMm = 70, BogenHoeheMm = 37;

    /// <summary>
    /// A4-Etikettenbogen 70×37 mm (3 × 8 = 24 je Seite, mehr Etiketten → weitere Seiten). Die Felder liegen genau auf den
    /// vorgestanzten Etiketten (3 × 70 = 210 mm Breite, 8 × 37 = 296 mm Höhe, oben 0,5 mm); gedruckt wird in « Taille réelle »
    /// (100 %). Ohne Rahmenlinien – sie würden bei kleinem Versatz auf dem Nachbar-Etikett landen. Der Inhalt hält Abstand
    /// zum Rand, weil Drucker die äußersten Millimeter des Blatts nicht bedrucken.
    /// </summary>
    public static byte[] ErzeugeA4Bogen(IReadOnlyList<Etikett> etiketten)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var seiten = etiketten.Chunk(BogenEtiketten).ToList();
        var dokument = Document.Create(container =>
        {
            foreach (var seite in seiten)
            {
                container.Page(page =>
                {
                    // Exakt 210 × 297 mm (PageSizes.A4 rechnet mit 595 pt – drei feste 70-mm-Spalten passten dann nicht);
                    // die drei gleichen Spalten (RelativeItem) sind damit genau 70 mm breit.
                    page.Size(BogenSpalten * BogenBreiteMm, 297, Unit.Millimetre);
                    page.MarginTop(0.5f, Unit.Millimetre);
                    page.MarginBottom(0);
                    page.MarginHorizontal(0);
                    page.DefaultTextStyle(x => x.FontSize(8));

                    page.Content().Column(spalte =>
                    {
                        for (var reihe = 0; reihe < BogenReihen; reihe++)
                        {
                            var start = reihe * BogenSpalten;
                            spalte.Item().Height(BogenHoeheMm, Unit.Millimetre).Row(zeile =>
                            {
                                for (var s = 0; s < BogenSpalten; s++)
                                {
                                    var index = start + s;
                                    var feld = zeile.RelativeItem().Height(BogenHoeheMm, Unit.Millimetre);
                                    if (index < seite.Length)
                                    {
                                        var etikett = seite[index];
                                        feld.PaddingVertical(4, Unit.Millimetre).PaddingHorizontal(5, Unit.Millimetre)
                                            .Element(c => EtikettInhalt(c, etikett, gross: true));
                                    }
                                }
                            });
                        }
                    });
                });
            }
        });

        return dokument.GeneratePdf();
    }

    /// <summary>Einzel-Etikett 62×29 mm (für Thermo-Etikettendrucker), auf Wunsch mehrfach (eine Seite je Etikett).</summary>
    public static byte[] ErzeugeEinzelEtikett(Etikett etikett, int anzahl = 1)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var dokument = Document.Create(container =>
        {
            for (var i = 0; i < Math.Max(1, anzahl); i++)
            {
                container.Page(page =>
                {
                    page.Size(62, 29, Unit.Millimetre);
                    page.Margin(1, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontSize(7));
                    page.Content().Width(60, Unit.Millimetre).Height(27, Unit.Millimetre)
                        .Element(c => EtikettZelle(c, etikett));
                });
            }
        });

        return dokument.GeneratePdf();
    }

    private static void EtikettZelle(IContainer container, Etikett etikett)
        => container
            .Border(0.5f)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(2)
            .Element(c => EtikettInhalt(c, etikett, gross: false));

    /// <summary>Name, Preis, Strich- oder QR-Code und Code in Klarschrift (größer auf dem 70×37-mm-Bogen).</summary>
    private static void EtikettInhalt(IContainer container, Etikett etikett, bool gross)
    {
        var barcode = ErzeugeCode128(etikett.Code);

        container.Column(col =>
        {
            col.Item().AlignCenter().Text(etikett.Name).FontSize(gross ? 8.5f : 7).Bold().ClampLines(1);
            if (!string.IsNullOrWhiteSpace(etikett.PreisText))
            {
                col.Item().AlignCenter().Text(etikett.PreisText).FontSize(gross ? 8 : 6.5f).FontColor(Colors.Grey.Darken1);
            }

            if (barcode is not null)
            {
                col.Item().AlignCenter().MaxHeight(gross ? 16 : 12, Unit.Millimetre).Svg(barcode).FitArea();
            }

            col.Item().AlignCenter().Text(etikett.Code.Length > 32 ? etikett.Code[..32] + "…" : etikett.Code).FontSize(gross ? 6.5f : 5.5f).FontColor(Colors.Grey.Darken1);
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
            // Lange Inhalte (Hersteller-QR, Web-Adressen) als QR-Code – Code 128 würde zu breit.
            var qr = inhalt.Length > ENote.Core.Services.Katalog.BarcodeRegeln.QrAbLaenge;
            var writer = new BarcodeWriterSvg
            {
                Format = qr ? BarcodeFormat.QR_CODE : BarcodeFormat.CODE_128,
                Options = qr
                    ? new EncodingOptions { Height = 120, Width = 120, Margin = 0, PureBarcode = true }
                    : new EncodingOptions { Height = 60, Width = 300, Margin = 2, PureBarcode = true }
            };
            return writer.Write(inhalt).Content;
        }
        catch
        {
            return null; // Barcode darf das PDF nie verhindern.
        }
    }
}
