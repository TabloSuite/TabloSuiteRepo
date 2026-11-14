using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Services.Organisation.Druck;

namespace Skoulou.WebApp.Services.Finanzen;

/// <summary>Plattformrechnung als PDF (A4): SKOULOU an die Schule, Periode, Schülerliste, Betrag in Worten, Zahlungshinweis.</summary>
internal static class RechnungPdf
{
    public static byte[] Erzeuge(RechnungDetail d, SchulDokument schule, string webseite) => Dokument(d, schule, webseite).GeneratePdf();

    public static IDocument Dokument(RechnungDetail d, SchulDokument schule, string webseite) => FinanzDruck.Lizenz(Document.Create(doc => doc.Page(page =>
    {
        var k = d.Kopf;
        DruckStil.Seite(page, PageSizes.A4, 14);
        page.Header().BorderBottom(1).BorderColor(DruckStil.RandZart).PaddingBottom(3, Unit.Millimetre).Row(row =>
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().Text("SKOULOU").FontSize(18).Bold().FontColor(DruckStil.Primaer).LetterSpacing(0.06f);
                c.Item().Text("Plateforme scolaire · TabloSuite").FontSize(8).FontColor(DruckStil.Leise);
                c.Item().Text(webseite).FontSize(8).FontColor(DruckStil.Leise);
            });
            row.ConstantItem(70, Unit.Millimetre).AlignRight().Column(c =>
            {
                c.Item().AlignRight().Text(k.IstNachtrag ? "FACTURE COMPLÉMENTAIRE" : "FACTURE").FontSize(14).Bold().FontColor(DruckStil.PrimaerDunkel);
                c.Item().AlignRight().Text($"N° {k.Nummer}").FontSize(10).SemiBold();
                c.Item().AlignRight().Text($"Date : {SkoulouKultur.Datum(k.Datum)} · Échéance : {SkoulouKultur.Datum(k.Faellig)}").FontSize(8.5f).FontColor(DruckStil.Leise);
                if (d.ErgaenztNummer is not null) c.Item().AlignRight().Text($"Complète la facture {d.ErgaenztNummer}").FontSize(8).FontColor(DruckStil.Leise);
            });
        });
        page.Content().PaddingTop(4, Unit.Millimetre).Layers(layers =>
        {
            layers.PrimaryLayer().Column(col =>
            {
                col.Spacing(3, Unit.Millimetre);
                col.Item().Row(row =>
                {
                    row.RelativeItem().Background(DruckStil.Flaeche).Padding(3, Unit.Millimetre).Column(c =>
                    {
                        c.Item().Text("Facturé à").FontSize(8).FontColor(DruckStil.Leise);
                        c.Item().Text(schule.Name).FontSize(11).Bold();
                        if (schule.Adresse is not null) c.Item().Text(schule.Adresse).FontSize(8.5f);
                        if (schule.Kontakt is not null) c.Item().Text(schule.Kontakt).FontSize(8.5f).FontColor(DruckStil.Leise);
                    });
                    row.ConstantItem(4, Unit.Millimetre);
                    row.RelativeItem().Background(DruckStil.Flaeche).Padding(3, Unit.Millimetre).Column(c =>
                    {
                        c.Item().Text("Objet").FontSize(8).FontColor(DruckStil.Leise);
                        c.Item().Text($"Utilisation de SKOULOU – {k.Periode} {k.Schuljahr}").FontSize(10).SemiBold();
                        c.Item().Text($"{k.Anzahl} élève{(k.Anzahl > 1 ? "s" : "")} × {SkoulouKultur.Fcfa(d.ProSchueler)} (part de la période du prix annuel figé)").FontSize(8.5f);
                    });
                });
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.ConstantColumn(10, Unit.Millimetre); c.RelativeColumn(4); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2); });
                    t.Header(h =>
                    {
                        foreach (var titel in new[] { "#", "Élève", "Matricule", "Classe", "Montant" })
                            h.Cell().BorderBottom(1).BorderColor(DruckStil.Rand).PaddingVertical(2).Text(titel).FontSize(8).SemiBold().FontColor(DruckStil.Leise);
                    });
                    var nr = 0;
                    foreach (var p in d.Positionen)
                    {
                        t.Cell().Element(Zelle).Text((++nr).ToString(SkoulouKultur.Kultur));
                        t.Cell().Element(Zelle).Text(p.StorniertGrund is null ? p.Name : $"{p.Name} (annulé)");
                        t.Cell().Element(Zelle).Text(p.Matrikel ?? "—");
                        t.Cell().Element(Zelle).Text(p.Klasse ?? "—");
                        t.Cell().Element(Zelle).AlignRight().Text(SkoulouKultur.Fcfa(p.Betrag));
                    }
                });
                col.Item().AlignRight().Width(80, Unit.Millimetre).Element(c => FinanzDruck.Kennzahl(c, "Total à payer", SkoulouKultur.Fcfa(k.Betrag), hervor: true));
                col.Item().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(9));
                    t.Span("Arrêtée la présente facture à la somme de ");
                    t.Span(ZahlInWorten.Fcfa(k.Betrag)).Italic().SemiBold();
                    t.Span(".");
                });
                col.Item().Background(DruckStil.PrimaerZart).Padding(3, Unit.Millimetre).Text(
                    $"Règlement par virement avant le {SkoulouKultur.Datum(k.Faellig)} en indiquant la référence {k.Nummer}. Après le virement, déclarez le paiement dans SKOULOU (Finances › Facturation SKOULOU). En cas de désaccord, contestez la facture au même endroit.")
                    .FontSize(8.5f).FontColor(DruckStil.PrimaerDunkel);
            });
            var stempel = k.Status switch
            {
                RechnungsStatus.Payee => ("PAYÉE", "#047857"),
                RechnungsStatus.Annulee => ("ANNULÉE", "#b91c1c"),
                _ => ((string?)null, (string?)null)
            };
            if (stempel.Item1 is { } text)
                layers.Layer().AlignCenter().AlignMiddle().Rotate(-15).Border(2).BorderColor(stempel.Item2!).Padding(4, Unit.Millimetre)
                    .Text(text).FontSize(34).Bold().FontColor(stempel.Item2!);
        });
        page.Footer().Element(c => DruckStil.Fuss(c, $"SKOULOU · facture {k.Nummer} · {schule.Name}"));
    })));

    private static IContainer Zelle(IContainer c) => c.BorderBottom(0.5f).BorderColor(DruckStil.RandZart).PaddingVertical(1.5f).DefaultTextStyle(s => s.FontSize(8.5f));
}
