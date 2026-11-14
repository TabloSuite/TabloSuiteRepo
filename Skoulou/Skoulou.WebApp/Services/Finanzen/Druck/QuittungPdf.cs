using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Services.Organisation.Druck;

namespace Skoulou.WebApp.Services.Finanzen;

/// <summary>
/// « Reçu de paiement » (A5 quer = halbe A4-Seite): Schulkopf mit Logo, fortlaufende Nummer, Betrag in Ziffern und Worten,
/// Verteilung auf Posten/Raten, Stand nach der Zahlung, Unterschrift. Stornierte Quittungen tragen « ANNULÉ ».
/// </summary>
internal static class QuittungPdf
{
    public static byte[] Erzeuge(QuittungDaten q) => Dokument(q).GeneratePdf();

    public static IDocument Dokument(QuittungDaten q) => FinanzDruck.Lizenz(Document.Create(doc => doc.Page(page =>
    {
        DruckStil.Seite(page, PageSizes.A5.Landscape(), 10);
        page.Header().Element(c => FinanzDruck.Kopf(c, q.Schule, "REÇU DE PAIEMENT", $"N° {q.Nummer} · {SkoulouKultur.Datum(q.Datum)}"));
        page.Content().PaddingTop(2, Unit.Millimetre).Layers(layers =>
        {
            layers.PrimaryLayer().Column(col =>
            {
                col.Spacing(2.5f, Unit.Millimetre);
                col.Item().Row(row =>
                {
                    row.RelativeItem(3).Column(c =>
                    {
                        c.Item().Text("Reçu de").FontSize(8).FontColor(DruckStil.Leise);
                        c.Item().Text(q.Schueler).FontSize(12).Bold().FontColor(DruckStil.PrimaerDunkel);
                        c.Item().Text(string.Join(" · ", new[] { q.Matrikel is null ? null : $"Matricule {q.Matrikel}", q.Klasse, $"Année {q.Schuljahr}" }
                            .Where(t => t is not null))).FontSize(8.5f).FontColor(DruckStil.Leise);
                    });
                    row.ConstantItem(62, Unit.Millimetre).Background(DruckStil.PrimaerZart).Padding(2.5f, Unit.Millimetre).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text("Montant reçu").FontSize(8).FontColor(DruckStil.Leise);
                        c.Item().AlignRight().Text(SkoulouKultur.Fcfa(q.Betrag)).FontSize(16).Bold().FontColor(DruckStil.Primaer);
                        c.Item().AlignRight().Text(q.Referenz is null ? q.Kanal : $"{q.Kanal} · réf. {q.Referenz}").FontSize(8).FontColor(DruckStil.Leise);
                    });
                });
                col.Item().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(9));
                    t.Span("Arrêté le présent reçu à la somme de ");
                    t.Span(ZahlInWorten.Fcfa(q.Betrag)).Italic().SemiBold();
                    t.Span(".");
                });
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(4);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                    });
                    table.Header(h =>
                    {
                        foreach (var titel in new[] { "Frais", "Tranche", "Échéance", "Montant imputé" })
                            h.Cell().BorderBottom(1).BorderColor(DruckStil.Rand).PaddingVertical(2).Text(titel).FontSize(8).SemiBold().FontColor(DruckStil.Leise);
                    });
                    foreach (var p in q.Verteilung)
                    {
                        table.Cell().Element(Zelle).Text(p.Bezeichnung);
                        table.Cell().Element(Zelle).Text(p.Tranche ?? "Unique");
                        table.Cell().Element(Zelle).Text(p.Faellig is { } f ? SkoulouKultur.Datum(f) : "—");
                        table.Cell().Element(Zelle).AlignRight().Text(SkoulouKultur.Fcfa(p.Betrag));
                    }
                    if (q.Guthaben > 0)
                    {
                        table.Cell().ColumnSpan(3).Element(Zelle).Text("Avoir (montant au-delà des frais dus)");
                        table.Cell().Element(Zelle).AlignRight().Text(SkoulouKultur.Fcfa(q.Guthaben));
                    }
                });
                col.Item().Row(row =>
                {
                    row.RelativeItem().Element(c => FinanzDruck.Kennzahl(c, "Total dû pour l'année", SkoulouKultur.Fcfa(q.SollJahr)));
                    row.ConstantItem(3, Unit.Millimetre);
                    row.RelativeItem().Element(c => FinanzDruck.Kennzahl(c, "Total versé", SkoulouKultur.Fcfa(q.BezahltBisher)));
                    row.ConstantItem(3, Unit.Millimetre);
                    row.RelativeItem().Element(c => FinanzDruck.Kennzahl(c, "Reste à payer", SkoulouKultur.Fcfa(q.RestNachher), hervor: true));
                });
                if (!string.IsNullOrWhiteSpace(q.Bemerkung))
                    col.Item().Text($"Observation : {q.Bemerkung}").FontSize(8.5f);
                col.Item().PaddingTop(1, Unit.Millimetre).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text($"Enregistré par {q.ErfasstVon}").FontSize(8).FontColor(DruckStil.Leise);
                        c.Item().Text($"Fait à {q.Schule.Ort}, le {SkoulouKultur.Datum(q.Datum)}").FontSize(8.5f);
                    });
                    row.ConstantItem(60, Unit.Millimetre).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text("Signature et cachet").FontSize(8).FontColor(DruckStil.Leise);
                        c.Item().Height(12, Unit.Millimetre);
                    });
                });
            });
            if (q.Storniert)
                layers.Layer().AlignCenter().AlignMiddle().Rotate(-18).Border(2).BorderColor("#b91c1c").Padding(4, Unit.Millimetre).Column(c =>
                {
                    c.Item().AlignCenter().Text("ANNULÉ").FontSize(30).Bold().FontColor("#b91c1c");
                    if (q.StornoGrund is not null) c.Item().AlignCenter().MaxWidth(90, Unit.Millimetre).Text(q.StornoGrund).FontSize(8).FontColor("#b91c1c");
                });
        });
        page.Footer().Element(c => DruckStil.Fuss(c, $"{q.Schule.Name} · reçu {q.Nummer} · édité avec SKOULOU"));
    })));

    private static IContainer Zelle(IContainer c) => c.BorderBottom(0.5f).BorderColor(DruckStil.RandZart).PaddingVertical(2).DefaultTextStyle(s => s.FontSize(8.5f));
}

/// <summary>Gemeinsame Bausteine der Finanz-PDFs.</summary>
internal static class FinanzDruck
{
    /// <summary>Community-Lizenz setzen (auch ohne App-Start, z. B. in Tests) und das Dokument durchreichen.</summary>
    public static IDocument Lizenz(IDocument dokument)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
        return dokument;
    }

    /// <summary>Schulkopf mit Logo, Adresse, Kontakt und Dokumenttitel rechts.</summary>
    public static void Kopf(IContainer container, SchulDokument s, string titel, string untertitel)
    {
        container.BorderBottom(1).BorderColor(DruckStil.RandZart).PaddingBottom(2.5f, Unit.Millimetre).Row(row =>
        {
            if (s.Logo is { } logo)
            {
                row.ConstantItem(15, Unit.Millimetre).Height(15, Unit.Millimetre).AlignMiddle().Image(logo).FitArea();
                row.ConstantItem(3, Unit.Millimetre);
            }
            row.RelativeItem().AlignMiddle().Column(col =>
            {
                col.Item().Text(s.Name).FontSize(11).Bold().FontColor(DruckStil.PrimaerDunkel);
                if (s.Slogan is not null) col.Item().Text(s.Slogan).FontSize(7.5f).Italic().FontColor(DruckStil.Leise);
                var zeile = string.Join(" · ", new[] { s.Adresse, s.Kontakt }.Where(t => !string.IsNullOrWhiteSpace(t)));
                if (zeile.Length > 0) col.Item().Text(zeile).FontSize(7.5f).FontColor(DruckStil.Leise);
            });
            row.ConstantItem(62, Unit.Millimetre).AlignRight().AlignMiddle().Column(col =>
            {
                col.Item().AlignRight().Text(titel).FontSize(12).Bold().FontColor(DruckStil.Primaer).LetterSpacing(0.04f);
                col.Item().AlignRight().Text(untertitel).FontSize(8.5f).FontColor(DruckStil.Leise);
            });
        });
    }

    public static void Kennzahl(IContainer container, string label, string wert, bool hervor = false)
        => container.Border(0.75f).BorderColor(hervor ? DruckStil.Primaer : DruckStil.Rand).Background(hervor ? DruckStil.PrimaerZart : Colors.White)
            .Padding(2, Unit.Millimetre).Column(c =>
            {
                c.Item().Text(label).FontSize(7.5f).FontColor(DruckStil.Leise);
                c.Item().Text(wert).FontSize(10.5f).Bold().FontColor(hervor ? DruckStil.Primaer : DruckStil.Text);
            });
}
