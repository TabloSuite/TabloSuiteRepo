using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>Allgemeines Bulletin-Layout (Muster Côte d'Ivoire/Togo). Mit SKOULOU_BULLETIN_VORSCHAU=Ordner werden PDF und PNG abgelegt.</summary>
public class BulletinStandardLayoutTests
{
    static BulletinStandardLayoutTests() => QuestPDF.Settings.License = LicenseType.Community;

    internal static BulletinSnapshot Beispiel(bool jahresabschluss)
        => BulletinBeispiel.Erzeugen(jahresabschluss, new BulletinLayout(true, true, true, true, false, true, true, true, true, true, true));

    private static Document Dokument(BulletinSnapshot b)
        => Document.Create(d => d.Page(page => BulletinStandardLayout.Seite(page, b, _ => { })));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Beispiel_ErzeugtEineA4Seite(bool jahresabschluss)
    {
        var dokument = Dokument(Beispiel(jahresabschluss));
        var pdf = dokument.GeneratePdf();
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        var bilder = dokument.GenerateImages(new ImageGenerationSettings { RasterDpi = 110 }).ToList();
        Assert.Single(bilder);

        var ordner = Environment.GetEnvironmentVariable("SKOULOU_BULLETIN_VORSCHAU");
        if (!string.IsNullOrWhiteSpace(ordner))
        {
            Directory.CreateDirectory(ordner);
            var name = jahresabschluss ? "bulletin-3e-trimestre" : "bulletin-1er-trimestre";
            File.WriteAllBytes(Path.Combine(ordner, name + ".pdf"), pdf);
            File.WriteAllBytes(Path.Combine(ordner, name + ".png"), bilder[0]);
        }
    }

    [Theory]
    [InlineData(11.02, "onze virgule zéro deux")]
    [InlineData(14.5, "quatorze virgule cinquante")]
    [InlineData(17.75, "dix-sept virgule soixante-quinze")]
    [InlineData(8, "huit")]
    public void InWorten_SchreibtDieMoyenneAus(decimal wert, string erwartet)
        => Assert.Equal(erwartet, BulletinTexte.InWorten(wert));

    [Theory]
    [InlineData(16.5, KlassenratDistinktion.Felicitations)]
    [InlineData(14, KlassenratDistinktion.Encouragements)]
    [InlineData(12.4, KlassenratDistinktion.TableauHonneur)]
    [InlineData(11.99, KlassenratDistinktion.Keine)]
    public void DistinktionVorschlag_FolgtDerMoyenne(decimal moyenne, KlassenratDistinktion erwartet)
        => Assert.Equal(erwartet, BulletinTexte.DistinktionVorschlag(moyenne));
}
