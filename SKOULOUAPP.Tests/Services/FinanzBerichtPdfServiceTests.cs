using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.Services;
using SkoulouApp.WebApp.Services;
using QuestPDF.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class FinanzBerichtPdfServiceTests
{
    [Fact]
    public void Darstellung_GruppiertNurGefilterteAbosUndZaehltDirektionsanteilNichtDoppelt()
    {
        var schoolId = Guid.NewGuid();
        var filtered = new FinanzUebersicht
        {
            Zeilen =
            [
                new() { Kategorie = "Scolarité", Richtung = FinanzRichtung.Einnahme, Betrag = 500m, KategorieSystemCode = FinanzSystemCode.KursNiveauZahlung },
                new() { Richtung = FinanzRichtung.Einnahme, Betrag = 1000m, KategorieSystemCode = FinanzSystemCode.SkoulouAppAboZahlung },
                new() { Richtung = FinanzRichtung.Einnahme, Betrag = 1000m, KategorieSystemCode = FinanzSystemCode.SkoulouAppAboZahlung },
                new() { Richtung = FinanzRichtung.Ausgabe, Betrag = 1700m, KategorieSystemCode = FinanzSystemCode.SkoulouAppAboAnteil },
                new() { Richtung = FinanzRichtung.Ausgabe, Betrag = 200m, KategorieSystemCode = FinanzSystemCode.SkoulouAppAboLehrerAnteil }
            ]
        };
        var filter = new FinanzFilter { SchulID = schoolId };

        var without = FinanzBerichtDarstellung.Erstellen(filtered, filter, false);
        Assert.Single(without.Zeilen);
        Assert.Equal(500m, without.Einnahmen);
        Assert.Equal(0m, without.AboEinnahmen);

        var with = FinanzBerichtDarstellung.Erstellen(filtered, filter, true);
        Assert.Single(with.Zeilen);
        Assert.Equal(2, with.AboZahlungenAnzahl);
        Assert.Equal(2000m, with.AboEinnahmen);
        Assert.Equal(1700m, with.AboAusgaben);
        Assert.Equal(200m, with.Lehreranteil);
        Assert.Equal(100m, with.Direktionsanteil);
        Assert.Equal(2500m, with.Einnahmen);
        Assert.Equal(800m, with.Saldo);
        Assert.Null(FinanzBerichtDarstellung.Erstellen(filtered, new FinanzFilter { SchulID = schoolId, Richtung = FinanzRichtung.Einnahme }, true).Direktionsanteil);
    }

    [Fact]
    public void Darstellung_ScreenshotZahlen_LehreranteilBleibtBeiDerSchule()
    {
        var uebersicht = new FinanzUebersicht
        {
            Zeilen =
            [
                new() { KategorieSystemCode = FinanzSystemCode.KursNiveauZahlung, Richtung = FinanzRichtung.Einnahme, Betrag = 50_000m },
                new() { KategorieSystemCode = FinanzSystemCode.KursNiveauZahlung, Richtung = FinanzRichtung.Einnahme, Betrag = 20_000m },
                .. Enumerable.Range(0, 3).Select(_ => new FinanzUebersichtZeile { KategorieSystemCode = FinanzSystemCode.SkoulouAppAboZahlung, Richtung = FinanzRichtung.Einnahme, Betrag = 12_500m }),
                .. Enumerable.Range(0, 3).Select(_ => new FinanzUebersichtZeile { KategorieSystemCode = FinanzSystemCode.SkoulouAppAboAnteil, Richtung = FinanzRichtung.Ausgabe, Betrag = 11_000m }),
                .. Enumerable.Range(0, 3).Select(_ => new FinanzUebersichtZeile { KategorieSystemCode = FinanzSystemCode.SkoulouAppAboLehrerAnteil, Richtung = FinanzRichtung.Ausgabe, Betrag = 1_000m })
            ]
        };
        var ohneAbos = FinanzBerichtDarstellung.Erstellen(uebersicht, new FinanzFilter(), false);
        var mitAbos = FinanzBerichtDarstellung.Erstellen(uebersicht, new FinanzFilter(), true);

        Assert.Equal(70_000m, ohneAbos.Saldo);
        Assert.Equal(107_500m, mitAbos.Einnahmen);
        Assert.Equal(33_000m, mitAbos.Ausgaben);
        Assert.Equal(74_500m, mitAbos.Saldo);
        Assert.Equal(37_500m, mitAbos.AboEinnahmen);
        Assert.Equal(33_000m, mitAbos.AboAusgaben);
        Assert.Equal(3_000m, mitAbos.Lehreranteil);
        Assert.Equal(1_500m, mitAbos.Direktionsanteil);
        Assert.Equal(3, mitAbos.AboZahlungenAnzahl);
        Assert.Equal(2, mitAbos.Zeilen.Count);

        var einnahmen = FinanzBerichtDarstellung.Erstellen(new FinanzUebersicht { Zeilen = uebersicht.Zeilen.Where(x => x.Richtung == FinanzRichtung.Einnahme).ToList() }, new FinanzFilter { Richtung = FinanzRichtung.Einnahme }, true);
        Assert.Equal(107_500m, einnahmen.Saldo);
        Assert.Null(einnahmen.Direktionsanteil);
        var ausgaben = FinanzBerichtDarstellung.Erstellen(new FinanzUebersicht { Zeilen = uebersicht.Zeilen.Where(x => x.Richtung == FinanzRichtung.Ausgabe).ToList() }, new FinanzFilter { Richtung = FinanzRichtung.Ausgabe }, true);
        Assert.Equal(-33_000m, ausgaben.Saldo);
        Assert.Null(ausgaben.Direktionsanteil);
    }

    [Fact]
    public void Generate_LiefertEchtesPdfFuerGefilterteBuchungen()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var report = new FinanzBericht("École de Lomé", new FinanzFilter
        {
            Von = new DateTime(2026, 10, 1),
            Bis = new DateTime(2026, 10, 31),
            Richtung = FinanzRichtung.Einnahme
        }, new FinanzUebersicht
        {
            SummeEinnahmen = 5000m,
            Zeilen = [new FinanzUebersichtZeile
            {
                Wirkungsdatum = new DateTime(2026, 10, 2),
                Kategorie = "Scolarité",
                Betrag = 5000m,
                Richtung = FinanzRichtung.Einnahme
            }]
        });
        var bytes = new FinanzBerichtPdfService().Generate(report);
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.Contains("Recettes", FinanzBerichtPdfService.DescribeFilter(report.Filter));
    }

    [Fact]
    public void Generate_MitAggregiertenAbos_ErzeugtPdfOhneEinzelzeilen()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var uebersicht = new FinanzUebersicht
        {
            Zeilen = Enumerable.Range(0, 1000).Select(_ => new FinanzUebersichtZeile
            {
                KategorieSystemCode = FinanzSystemCode.SkoulouAppAboZahlung,
                Richtung = FinanzRichtung.Einnahme,
                Betrag = 1000m,
                Wirkungsdatum = DateTime.Today
            }).ToList()
        };
        var bericht = new FinanzBericht("École", new FinanzFilter(), uebersicht, true);
        Assert.Empty(bericht.Darstellung.Zeilen);
        Assert.Equal(1000, bericht.Darstellung.AboZahlungenAnzahl);
        var bytes = new FinanzBerichtPdfService().Generate(bericht);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.True(bytes.Length > 1000);
    }
}
