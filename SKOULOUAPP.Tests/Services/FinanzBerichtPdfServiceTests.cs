using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.Services;
using SkoulouApp.WebApp.Services;
using QuestPDF.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class FinanzBerichtPdfServiceTests
{
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
}
