using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Salons;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ENote.WebApp.Services;

/// <summary>Erzeugt einen Kassenbon (Verkaufsbeleg) als PDF.</summary>
public static class BonPdfService
{
    public static byte[] ErzeugeBon(
        Verkauf verkauf,
        Salon salon,
        string kassiererName,
        byte[]? logoBytes = null,
        string? qrLinkZiel = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var dokument = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(80, 297, Unit.Millimetre); // Bon-Rollenbreite, A4-Höhe als ausreichende Fläche
                page.Margin(6, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Content().Column(col =>
                {
                    col.Spacing(4);

                    if (logoBytes is { Length: > 0 })
                    {
                        col.Item().AlignCenter().MaxHeight(60).Image(logoBytes).FitArea();
                        col.Item().PaddingTop(2);
                    }

                    col.Item().AlignCenter().Text(salon.Name).FontSize(13).Bold();

                    if (!string.IsNullOrWhiteSpace(salon.Slogan))
                    {
                        col.Item().AlignCenter().Text($"« {salon.Slogan} »").FontSize(8).Italic();
                    }

                    if (!string.IsNullOrWhiteSpace(salon.Adresse))
                    {
                        col.Item().AlignCenter().Text(salon.Adresse).FontSize(8);
                    }
                    else if (!string.IsNullOrWhiteSpace(salon.Standort))
                    {
                        col.Item().AlignCenter().Text(salon.Standort).FontSize(8);
                    }

                    if (!string.IsNullOrWhiteSpace(salon.TelefonNummer))
                    {
                        col.Item().AlignCenter().Text($"Tél : {salon.TelefonNummer}").FontSize(8);
                    }

                    col.Item().PaddingTop(4).LineHorizontal(0.5f);

                    if (verkauf.StornoVonVerkaufId is Guid origId)
                    {
                        col.Item().AlignCenter().Text("REMBOURSEMENT").Bold().FontSize(11);
                        col.Item().Text($"Vente d'origine n° {origId.ToString()[..8].ToUpperInvariant()}").FontSize(8);
                        col.Item().Text($"Motif : {verkauf.StornoGrund}").FontSize(8);
                    }
                    col.Item().Text($"Reçu n° {verkauf.Id.ToString()[..8].ToUpperInvariant()}").FontSize(8);
                    col.Item().Text(verkauf.ZeitUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm")).FontSize(8);
                    col.Item().Text($"Servi par : {kassiererName}").FontSize(8);
                    if (verkauf.Kunde is { } kunde)
                        col.Item().Text($"Client : {kunde.Name}").FontSize(8);

                    col.Item().PaddingTop(4).LineHorizontal(0.5f);

                    foreach (var pos in verkauf.Positionen)
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text(text =>
                            {
                                text.Span($"{pos.Menge}× {pos.LeistungName}");
                                if (pos.RabattBetrag > 0)
                                {
                                    text.Span($"  (-{pos.RabattBetrag:N0} F)").FontColor(Colors.Green.Medium);
                                }
                            });
                            row.ConstantItem(60).AlignRight().Text($"{pos.ZeilenSumme:N0} F");
                        });
                    }

                    col.Item().PaddingTop(4).LineHorizontal(0.5f);

                    if (verkauf.RabattSumme > 0)
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text("Remise");
                            row.ConstantItem(60).AlignRight().Text($"-{verkauf.RabattSumme:N0} F").FontColor(Colors.Green.Medium);
                        });
                    }

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text("TOTAL").Bold().FontSize(11);
                        row.ConstantItem(70).AlignRight().Text($"{verkauf.GesamtSumme:N0} F").Bold().FontSize(11);
                    });

                    var anteile = ENote.Core.Services.Kasse.KassenRechner.Anteile(verkauf);
                    if (anteile.Count == 1)
                    {
                        col.Item().PaddingTop(4).Text($"{(verkauf.IstStorno ? "Remboursé en" : "Paiement")} : {ZahlungsTexte.Name(anteile[0])}").FontSize(8);
                    }
                    else
                    {
                        col.Item().PaddingTop(4).Text("Paiement :").FontSize(8);
                        foreach (var anteil in anteile)
                        {
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Text($"  {ZahlungsTexte.Name(anteil)}").FontSize(8);
                                row.ConstantItem(70).AlignRight().Text($"{anteil.Betrag:N0} F").FontSize(8);
                            });
                        }
                    }

                    if (verkauf.Gegeben is not null)
                    {
                        col.Item().Text($"Espèces reçues : {verkauf.Gegeben:N0} F   Monnaie : {verkauf.Rueckgeld:N0} F").FontSize(8);
                    }

                    if (!string.IsNullOrWhiteSpace(qrLinkZiel))
                    {
                        var qrBytes = ErzeugeQrCode(qrLinkZiel);
                        if (qrBytes is not null)
                        {
                            col.Item().PaddingTop(8).AlignCenter().MaxHeight(90).Image(qrBytes).FitArea();
                        }
                    }

                    col.Item().PaddingTop(8).AlignCenter().Text(verkauf.IstStorno ? "Document de remboursement – à conserver." : "Merci de votre visite !").FontSize(9).Italic();
                });
            });
        });

        return dokument.GeneratePdf();
    }

    /// <summary>Erzeugt einen QR-Code als PNG-Bytes.</summary>
    private static byte[]? ErzeugeQrCode(string inhalt)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var daten = generator.CreateQrCode(inhalt, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(daten);
            return png.GetGraphic(20);
        }
        catch
        {
            return null; // QR darf den Bon nie verhindern.
        }
    }
}
