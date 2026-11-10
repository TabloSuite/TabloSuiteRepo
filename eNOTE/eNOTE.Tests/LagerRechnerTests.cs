using ENote.Core.DataModels.Katalog;
using ENote.Core.Services.Katalog;
using Xunit;

namespace ENote.Tests;

public class LagerRechnerTests
{
    [Fact]
    public void Bewertet_Nur_Lagerartikel_Und_Rechnet_Gewinn_Nur_Mit_Einkaufspreis()
    {
        var bewertung = LagerRechner.Bewerte(
        [
            new Leistung { Name = "Huile", Preis = 3000, Kaufpreis = 1800, HatStueckzahl = true, Stueckzahl = 10, IstAktiv = true },
            new Leistung { Name = "Mèches", Preis = 2500, Kaufpreis = 1500, HatStueckzahl = true, Stueckzahl = 4, IstAktiv = true },
            new Leistung { Name = "Café", Preis = 500, Kaufpreis = null, HatStueckzahl = true, Stueckzahl = 20, IstAktiv = true }, // ohne EK
            new Leistung { Name = "Négatif", Preis = 1000, Kaufpreis = 500, HatStueckzahl = true, Stueckzahl = -3, IstAktiv = true },
            new Leistung { Name = "Inactif", Preis = 1000, Kaufpreis = 500, HatStueckzahl = true, Stueckzahl = 9, IstAktiv = false },
            new Leistung { Name = "Coupe", Preis = 1500, HatStueckzahl = false, IstAktiv = true },
            new Leistung { Name = "Divers", Preis = 0, HatStueckzahl = true, Stueckzahl = 5, IstAktiv = true, IstStandardArtikel = true },
        ]);

        Assert.Equal(3, bewertung.Artikel);
        Assert.Equal(34m, bewertung.Stueck);
        Assert.Equal(18000m + 6000m, bewertung.Einkaufswert);
        Assert.Equal(30000m + 10000m + 10000m, bewertung.Verkaufswert);
        Assert.Equal(12000m + 4000m, bewertung.Gewinn);         // Café ohne EK zählt nicht zum Gewinn
        Assert.Equal(40m, bewertung.MargeProzent);              // 16.000 / 40.000
        Assert.Equal(1, bewertung.OhneEinkaufspreis);
        Assert.Null(bewertung.Positionen.Single(p => p.Name == "Café").Gewinn);
    }
}
