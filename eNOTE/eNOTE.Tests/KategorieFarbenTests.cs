using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Katalog;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

public class KategorieFarbenTests
{
    private static LeistungKategorie Kat(string name, string? farbe = null, Guid? parentId = null)
        => new() { Name = name, Farbe = farbe, ParentId = parentId };

    [Fact]
    public void Ohne_Gewaehlte_Farbe_Bleibt_Die_Automatische_Reihenfolge()
    {
        var boissons = Kat("Boissons");
        var tissus = Kat("Tissus");
        var kategorien = new List<LeistungKategorie> { tissus, boissons };

        // Nach Namen sortiert: Boissons = 1. Palettenfarbe, Tissus = 2.
        Assert.Equal(KategorieFarben.Palette[0], KategorieFarben.Ermittle(kategorien, boissons.Id));
        Assert.Equal(KategorieFarben.Palette[1], KategorieFarben.Ermittle(kategorien, tissus.Id));
        Assert.Equal(KategorieFarben.Neutral, KategorieFarben.Ermittle(kategorien, null));
        Assert.Equal(KategorieFarben.Neutral, KategorieFarben.Ermittle(kategorien, Guid.NewGuid()));
    }

    [Fact]
    public void Gewaehlte_Farbe_Gilt_Und_Unterkategorien_Erben_Sie()
    {
        var soins = Kat("Soins", "#B91C1C");
        var visage = Kat("Visage", parentId: soins.Id);
        var cheveux = Kat("Cheveux", "#475569", soins.Id);
        var kategorien = new List<LeistungKategorie> { soins, visage, cheveux };

        Assert.Equal("#b91c1c", KategorieFarben.Ermittle(kategorien, soins.Id));
        Assert.Equal("#b91c1c", KategorieFarben.Ermittle(kategorien, visage.Id)); // erbt
        Assert.Equal("#475569", KategorieFarben.Ermittle(kategorien, cheveux.Id)); // eigene Farbe
    }

    [Fact]
    public void Unterkategorie_Ohne_Farbe_Erbt_Automatische_Farbe_Der_Hauptkategorie()
    {
        var accessoires = Kat("Accessoires");
        var bijoux = Kat("Bijoux", parentId: accessoires.Id);
        var kategorien = new List<LeistungKategorie> { accessoires, bijoux };

        Assert.Equal(KategorieFarben.Palette[0], KategorieFarben.Ermittle(kategorien, bijoux.Id));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("#2547B8", "#2547b8")]
    [InlineData(" #0e7490 ", "#0e7490")]
    [InlineData("rot", null)]
    [InlineData("#fff", null)]
    [InlineData("#12345g", null)]
    public void Normalisiere_Akzeptiert_Nur_Sechsstellige_Hexfarben(string? eingabe, string? erwartet)
        => Assert.Equal(erwartet, KategorieFarben.Normalisiere(eingabe));

    [Fact]
    public void Auswahl_Ist_Gueltig_Und_Eindeutig()
    {
        Assert.All(KategorieFarben.Auswahl, f => Assert.Equal(f.Hex, KategorieFarben.Normalisiere(f.Hex)));
        Assert.Equal(KategorieFarben.Auswahl.Count, KategorieFarben.Auswahl.Select(f => f.Hex).Distinct().Count());
        Assert.All(KategorieFarben.Palette, hex => Assert.Contains(KategorieFarben.Auswahl, f => f.Hex == hex));
    }

    [Fact]
    public async Task AktualisiereKategorie_Speichert_Name_Und_Farbe_Und_Setzt_Auf_Automatisch_Zurueck()
    {
        var factory = TestDbContextFactory.Create();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(new Salon { Name = "Läden A", IstFreigegeben = true });
            db.Users.Add(new AppUser { UserName = "u@x.de", Email = "u@x.de", FullName = "User" });
            await db.SaveChangesAsync();
        }

        Guid salonId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            salonId = (await db.Salons.FirstAsync()).Id;
        }

        var service = new KatalogService(factory, userManager: null!, mediaStorage: null!);
        var kategorie = await service.SpeichereKategorieAsync(salonId, "Soins");

        var (erfolg, fehler) = await service.AktualisiereKategorieAsync(kategorie.Id, salonId, "Soins visage", "#B91C1C");
        Assert.True(erfolg, fehler);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var gespeichert = await db.LeistungKategorien.SingleAsync(k => k.Id == kategorie.Id);
            Assert.Equal("Soins visage", gespeichert.Name);
            Assert.Equal("#b91c1c", gespeichert.Farbe);
        }

        // Nur umbenennen lässt die Farbe stehen.
        Assert.True((await service.BenenneKategorieUmAsync(kategorie.Id, salonId, "Soins")).Erfolg);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal("#b91c1c", (await db.LeistungKategorien.SingleAsync(k => k.Id == kategorie.Id)).Farbe);
        }

        // Ungültige Farbe wird abgelehnt, nichts geändert.
        var (ok, meldung) = await service.AktualisiereKategorieAsync(kategorie.Id, salonId, "Soins", "javascript:alert(1)");
        Assert.False(ok);
        Assert.Equal("Couleur invalide.", meldung);

        // Leer = wieder automatisch.
        Assert.True((await service.AktualisiereKategorieAsync(kategorie.Id, salonId, "Soins", null)).Erfolg);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Null((await db.LeistungKategorien.SingleAsync(k => k.Id == kategorie.Id)).Farbe);
        }
    }
}
