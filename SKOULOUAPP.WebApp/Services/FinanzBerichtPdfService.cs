using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SkoulouApp.WebApp.Services;

public sealed class FinanzBerichtPdfService
{
    public byte[] Generate(FinanzBericht bericht)
    {
        var darstellung = bericht.Darstellung;
        var rows = darstellung.Zeilen;
        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(18);
            page.DefaultTextStyle(text => text.FontSize(10));
            page.Header().Column(header =>
            {
                header.Item().Text("SKOULOU · Finances de l'école").SemiBold().FontColor(Colors.Blue.Darken2);
                header.Item().Text($"Rapport financier · {bericht.SchulName}").Bold().FontSize(20);
                header.Item().Text(DescribeFilter(bericht.Filter)).FontColor(Colors.Grey.Darken2);
                header.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Blue.Medium);
            });
            page.Content().PaddingTop(16).Column(content =>
            {
                content.Spacing(12);
                content.Item().Row(summary =>
                {
                    summary.RelativeItem().Text($"Recettes : {darstellung.Einnahmen:N0} FCFA").SemiBold().FontColor(Colors.Green.Darken2);
                    summary.RelativeItem().Text($"Dépenses école + part plateforme : {darstellung.Ausgaben:N0} FCFA").SemiBold().FontColor(Colors.Red.Darken2);
                });
                content.Item().Text($"Solde revenant à l'école : {darstellung.Saldo:N0} FCFA").Bold().FontSize(15);
                if (bericht.MitAbos && darstellung.HatAboBuchungen)
                {
                    content.Item().Background(Colors.Blue.Lighten5).Padding(10).Column(abo =>
                    {
                        abo.Item().Text($"Abonnements SKOULOUAPP · {darstellung.AboZahlungenAnzahl} paiement(s) regroupé(s)").Bold();
                        abo.Item().Text($"Recettes : {darstellung.AboEinnahmen:N0} FCFA · Part plateforme déduite : {darstellung.AboAusgaben:N0} FCFA");
                        abo.Item().Text($"Part enseignants conservée : {darstellung.Lehreranteil:N0} FCFA");
                        abo.Item().Text(darstellung.Direktionsanteil is decimal part
                            ? $"Part direction (information, non ajoutée aux recettes) : {part:N0} FCFA"
                            : "Part direction complète : sélectionner recettes et dépenses.");
                    });
                }
                content.Item().Text("Écritures détaillées").SemiBold();
                content.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(66);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn();
                        columns.ConstantColumn(95);
                    });
                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCell).Text("Date");
                        header.Cell().Element(HeaderCell).Text("Catégorie");
                        header.Cell().Element(HeaderCell).Text("N° écriture / justificatif");
                        header.Cell().Element(HeaderCell).Text("Type");
                        header.Cell().Element(HeaderCell).AlignRight().Text("Montant (FCFA)");
                    });
                    foreach (var row in rows)
                    {
                        table.Cell().Element(BodyCell).Text(row.Wirkungsdatum.ToString("dd/MM/yyyy"));
                        table.Cell().Element(BodyCell).Text(row.KategorieSystemCode == FinanzSystemCode.KursNiveauZahlung ? "Scolarité" : row.Kategorie);
                        table.Cell().Element(BodyCell).Text(row.Buchungsnummer
                            + (string.IsNullOrWhiteSpace(row.Belegnummer) ? "" : $"\nRéférence : {row.Belegnummer}")
                            + (row.HatBelegdatei ? $"\nFichier : {row.BelegDateiname}" : row.KategorieSystemCode == FinanzSystemCode.KursNiveauZahlung
                                ? "\nPaiement de cycle" : row.KategorieSystemCode is null or FinanzSystemCode.Keiner
                                ? "\nSans document joint" : "\nÉcriture automatique"));
                        table.Cell().Element(BodyCell).Text(row.Richtung == FinanzRichtung.Einnahme ? "Recette" : "Dépense");
                        table.Cell().Element(BodyCell).AlignRight().Text($"{(row.Richtung == FinanzRichtung.Einnahme ? "+" : "−")}{row.Betrag:N0}");
                    }
                });
                if (rows.Count == 0)
                    content.Item().Text("Aucune écriture détaillée pour les filtres sélectionnés.").Italic();
            });
            page.Footer().AlignRight().Text(text =>
            {
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        })).GeneratePdf();
    }

    public static string DescribeFilter(FinanzFilter filter)
    {
        var from = filter.Von?.ToString("dd/MM/yyyy") ?? "début";
        var to = filter.Bis?.ToString("dd/MM/yyyy") ?? "aujourd'hui";
        var direction = filter.Richtung switch
        {
            FinanzRichtung.Einnahme => "Recettes",
            FinanzRichtung.Ausgabe => "Dépenses",
            _ => "Recettes et dépenses"
        };
        var sorting = filter.Sortierung switch
        {
            FinanzSortierung.WirkungsdatumAufsteigend => "Date croissante",
            FinanzSortierung.BetragAbsteigend => "Montant décroissant",
            FinanzSortierung.BetragAufsteigend => "Montant croissant",
            FinanzSortierung.ErstelltAmAbsteigend => "Saisie récente",
            _ => "Date décroissante"
        };
        return $"Du {from} au {to} · {direction} · {sorting}";
    }

    private static IContainer HeaderCell(IContainer container)
        => container.Background(Colors.Blue.Lighten4).PaddingVertical(7).PaddingHorizontal(4).DefaultTextStyle(text => text.SemiBold());

    private static IContainer BodyCell(IContainer container)
        => container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(6).PaddingHorizontal(4);
}
