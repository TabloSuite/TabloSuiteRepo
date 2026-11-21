using ENote.Core.DataModels.Salons;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ENote.WebApp.Services;

/// <summary>Erzeugt einen Finanzbericht (Umsatz über einen Zeitraum) als A4-PDF.</summary>
public static class FinanzBerichtPdfService
{
    public static byte[] ErzeugeBericht(
        Salon salon,
        DateTime von,
        DateTime bis,
        IReadOnlyList<FinanzService.TagesUmsatz> tage,
        FinanzService.FinanzUebersicht? uebersicht = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var gesamtBar = tage.Sum(t => t.Bar);
        var gesamtKarte = tage.Sum(t => t.Karte);
        var gesamtMobile = tage.Sum(t => t.MobileMoney);
        var gesamt = tage.Sum(t => t.Gesamt);
        var anzahl = tage.Sum(t => t.AnzahlVerkaeufe);
        var ausgaben = uebersicht?.AusgabenGesamt ?? 0m;
        var saldo = uebersicht?.Saldo ?? gesamt - ausgaben;

        var dokument = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("Rapport financier").FontSize(20).Bold();
                    col.Item().Text(salon.Name).FontSize(12).SemiBold();
                    col.Item().Text($"Période : du {von:dd.MM.yyyy} au {bis:dd.MM.yyyy}").FontSize(9).FontColor(Colors.Grey.Medium);
                });

                page.Content().PaddingTop(15).Column(col =>
                {
                    col.Spacing(10);

                    // Zusammenfassung
                    col.Item().Row(row =>
                    {
                        ZusammenfassungKarte(row.RelativeItem(), "Total", $"{gesamt:N0} F", true);
                        ZusammenfassungKarte(row.RelativeItem(), "Espèces", $"{gesamtBar:N0} F", false);
                        ZusammenfassungKarte(row.RelativeItem(), "Carte", $"{gesamtKarte:N0} F", false);
                        ZusammenfassungKarte(row.RelativeItem(), "Mobile Money", $"{gesamtMobile:N0} F", false);
                        ZusammenfassungKarte(row.RelativeItem(), "Ventes", anzahl.ToString(), false);
                    });

                    // Solde de caisse: Entrées - Dépenses = Solde
                    col.Item().Row(row =>
                    {
                        ZusammenfassungKarte(row.RelativeItem(), "Entrées (ventes)", $"{gesamt:N0} F", false);
                        ZusammenfassungKarte(row.RelativeItem(), "Dépenses", $"{ausgaben:N0} F", false);
                        if (uebersicht is { Einlagen: > 0 } || uebersicht is { EntnahmenBar: > 0 })
                        {
                            ZusammenfassungKarte(row.RelativeItem(), "Apports / retraits", $"+{uebersicht!.Einlagen:N0} / −{uebersicht.EntnahmenBar:N0} F", false);
                        }
                        ZusammenfassungKarte(row.RelativeItem(), "Solde de caisse", $"{(saldo < 0 ? "-" : "")}{Math.Abs(saldo):N0} F", true);
                    });

                    if (uebersicht is not null && uebersicht.MobileMoneyNachAnbieter.Count > 0)
                    {
                        col.Item().PaddingTop(4).Text("Mobile Money par opérateur").SemiBold().FontSize(11);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(3);
                                cols.RelativeColumn();
                            });

                            foreach (var a in uebersicht.MobileMoneyNachAnbieter)
                            {
                                table.Cell().Element(DatenZelle).Text(a.Name);
                                table.Cell().Element(DatenZelle).AlignRight().Text($"{a.Betrag:N0} F");
                            }
                        });
                    }

                    if (uebersicht is not null && uebersicht.AusgabenNachKategorie.Count > 0)
                    {
                        col.Item().PaddingTop(4).Text("Dépenses par catégorie").SemiBold().FontSize(11);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(3);
                                cols.RelativeColumn();
                            });

                            foreach (var k in uebersicht.AusgabenNachKategorie)
                            {
                                table.Cell().Element(DatenZelle).Text(k.Name);
                                table.Cell().Element(DatenZelle).AlignRight().Text($"{k.Betrag:N0} F");
                            }
                        });
                    }

                    // Tabelle
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(2); // Date
                            cols.RelativeColumn();  // Ventes
                            cols.RelativeColumn();  // Espèces
                            cols.RelativeColumn();  // Carte
                            cols.RelativeColumn();  // Mobile Money
                            cols.RelativeColumn();  // Total
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(KopfZelle).Text("Date");
                            header.Cell().Element(KopfZelle).AlignRight().Text("Ventes");
                            header.Cell().Element(KopfZelle).AlignRight().Text("Espèces");
                            header.Cell().Element(KopfZelle).AlignRight().Text("Carte");
                            header.Cell().Element(KopfZelle).AlignRight().Text("Mobile M.");
                            header.Cell().Element(KopfZelle).AlignRight().Text("Total");
                        });

                        foreach (var t in tage.OrderBy(x => x.Datum))
                        {
                            table.Cell().Element(DatenZelle).Text(t.Datum.ToString("dd.MM.yyyy"));
                            table.Cell().Element(DatenZelle).AlignRight().Text(t.AnzahlVerkaeufe.ToString());
                            table.Cell().Element(DatenZelle).AlignRight().Text($"{t.Bar:N0} F");
                            table.Cell().Element(DatenZelle).AlignRight().Text($"{t.Karte:N0} F");
                            table.Cell().Element(DatenZelle).AlignRight().Text($"{t.MobileMoney:N0} F");
                            table.Cell().Element(DatenZelle).AlignRight().Text($"{t.Gesamt:N0} F").SemiBold();
                        }
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("eNOTE · Généré le " + ENote.Core.Services.Zeitzonen.ImLaden(ENote.Core.Services.AppUhr.UtcNow, ENote.Core.Services.Zeitzonen.Fuer(salon.Land)).ToString("dd.MM.yyyy HH:mm") + " · Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                    t.CurrentPageNumber().FontSize(8);
                    t.Span(" / ").FontSize(8);
                    t.TotalPages().FontSize(8);
                });
            });
        });

        return dokument.GeneratePdf();

        static IContainer KopfZelle(IContainer c) => c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).DefaultTextStyle(x => x.SemiBold().FontSize(9));
        static IContainer DatenZelle(IContainer c) => c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten4).PaddingVertical(3);

        static void ZusammenfassungKarte(IContainer container, string titel, string wert, bool hervorgehoben)
        {
            container
                .Border(1)
                .BorderColor(hervorgehoben ? Colors.Indigo.Medium : Colors.Grey.Lighten2)
                .Padding(8)
                .Column(c =>
                {
                    c.Item().Text(titel).FontSize(8).FontColor(Colors.Grey.Darken1);
                    c.Item().Text(wert).FontSize(13).Bold().FontColor(hervorgehoben ? Colors.Indigo.Medium : Colors.Black);
                });
        }
    }
}
