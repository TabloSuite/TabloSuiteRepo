using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Skoulou.Core.Werkzeuge;

namespace Skoulou.WebApp.Services.Organisation.Druck;

/// <summary>
/// Kartenbogen (A4, 2 Spalten, Karten 85,6 × 54 mm wie eine Bankkarte): Kopf mit Logo und Schule, Foto oder
/// Initialen, « NOM Prénom », Klasse, Matrikel, Geburtsdatum, QR zur Prüfseite (ohne Personendaten).
/// </summary>
internal static class SchuelerKartenPdf
{
    private const float Breite = 85.6f;
    private const float Hoehe = 54f;

    public static byte[] Erzeuge(KartenBogen bogen, byte[]? logo, IReadOnlyDictionary<Guid, byte[]> fotos, Func<string, string> pruefUrl)
        => Dokument(bogen, logo, fotos, pruefUrl).GeneratePdf();

    /// <summary>Dokument (für PDF oder Vorschaubilder in Tests).</summary>
    public static IDocument Dokument(KartenBogen bogen, byte[]? logo, IReadOnlyDictionary<Guid, byte[]> fotos, Func<string, string> pruefUrl)
        => Document.Create(doc => doc.Page(page =>
        {
            DruckStil.Seite(page, PageSizes.A4, 12);
            page.MarginHorizontal(15, Unit.Millimetre);
            page.Content().Column(col =>
            {
                col.Spacing(6, Unit.Millimetre);
                if (bogen.Karten.Count == 0)
                {
                    col.Item().Element(c => DruckStil.Kopf(c, new SchulKopf(bogen.Schule, logo), $"Cartes d'élève · {bogen.Klasse}", $"Année scolaire {bogen.Jahr}"));
                    col.Item().PaddingTop(10, Unit.Millimetre).AlignCenter().Text("Aucun élève actif dans cette classe.").FontSize(12).FontColor(DruckStil.Leise);
                    return;
                }
                foreach (var paar in bogen.Karten.Chunk(2))
                {
                    col.Item().ShowEntire().Row(row =>
                    {
                        row.ConstantItem(Breite, Unit.Millimetre).Element(c => Karte(c, bogen, logo, paar[0], fotos, pruefUrl));
                        row.ConstantItem(8, Unit.Millimetre);
                        if (paar.Length > 1) row.ConstantItem(Breite, Unit.Millimetre).Element(c => Karte(c, bogen, logo, paar[1], fotos, pruefUrl));
                    });
                }
            });
        }));

    private static void Karte(IContainer container, KartenBogen bogen, byte[]? logo, KarteDaten k, IReadOnlyDictionary<Guid, byte[]> fotos, Func<string, string> pruefUrl)
    {
        container.Width(Breite, Unit.Millimetre).Height(Hoehe, Unit.Millimetre).Border(0.6f).BorderColor(DruckStil.Rand).Column(col =>
        {
            // Kopfband
            col.Item().Height(11, Unit.Millimetre).Background(DruckStil.Primaer).PaddingHorizontal(2.5f, Unit.Millimetre).Row(row =>
            {
                if (logo is not null)
                {
                    row.ConstantItem(8.5f, Unit.Millimetre).AlignMiddle().Height(8.5f, Unit.Millimetre).Background(Colors.White).Padding(0.5f, Unit.Millimetre).Image(logo).FitArea();
                    row.ConstantItem(2, Unit.Millimetre);
                }
                row.RelativeItem().AlignMiddle().Column(c =>
                {
                    c.Item().Text(bogen.Schule).FontSize(7.5f).Bold().FontColor(Colors.White).ClampLines(1);
                    c.Item().Text($"CARTE D'ÉLÈVE · {bogen.Jahr}").FontSize(6).SemiBold().FontColor(Colors.White).LetterSpacing(0.05f);
                });
            });

            // Inhalt: Foto | Angaben | QR
            col.Item().Height(34, Unit.Millimetre).PaddingHorizontal(2.5f, Unit.Millimetre).PaddingTop(2.5f, Unit.Millimetre).Row(row =>
            {
                row.ConstantItem(20, Unit.Millimetre).Column(c =>
                {
                    if (fotos.TryGetValue(k.SchuelerId, out var foto))
                        c.Item().Width(20, Unit.Millimetre).Height(25, Unit.Millimetre).Border(0.4f).BorderColor(DruckStil.RandZart).Image(foto).FitArea();
                    else
                        c.Item().PaddingTop(2, Unit.Millimetre).Element(e => DruckStil.Initialen(e, k.Vorname, k.Nachname, 20));
                });
                row.ConstantItem(2.5f, Unit.Millimetre);
                row.RelativeItem().Column(c =>
                {
                    c.Spacing(0.6f, Unit.Millimetre);
                    c.Item().PaddingBottom(1, Unit.Millimetre).Text(text =>
                    {
                        text.DefaultTextStyle(s => s.FontSize(9).FontColor(DruckStil.PrimaerDunkel));
                        text.Span(k.Nachname.ToUpper(SkoulouKultur.Kultur)).Bold();
                        text.Span(" " + k.Vorname).SemiBold();
                    });
                    Angabe(c, "Classe", bogen.Klasse);
                    Angabe(c, "Matricule", k.Matrikel ?? "–");
                    if (k.Geburtsdatum is { } g) Angabe(c, "Né(e) le", SkoulouKultur.Datum(g));
                });
                row.ConstantItem(17, Unit.Millimetre).AlignTop().Width(17, Unit.Millimetre).Height(17, Unit.Millimetre).Image(Qr(pruefUrl(k.Token))).FitArea();
            });

            // Fuß
            col.Item().Height(9, Unit.Millimetre).BorderTop(0.4f).BorderColor(DruckStil.RandZart).PaddingHorizontal(2.5f, Unit.Millimetre).Row(row =>
            {
                row.RelativeItem().AlignMiddle().Text($"Valable pour l'année scolaire {bogen.Jahr}").FontSize(5.5f).FontColor(DruckStil.Leise);
                row.RelativeItem().AlignMiddle().AlignRight().Text("Signature de la direction").FontSize(5.5f).Italic().FontColor(DruckStil.Leise);
            });
        });
    }

    private static void Angabe(ColumnDescriptor col, string label, string wert)
        => col.Item().Text(text =>
        {
            text.DefaultTextStyle(s => s.FontSize(7.5f));
            text.Span(label + " : ").FontColor(DruckStil.Leise);
            text.Span(wert).SemiBold().FontColor(DruckStil.Text);
        });

    private static byte[] Qr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var daten = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(daten).GetGraphic(8);
    }
}
