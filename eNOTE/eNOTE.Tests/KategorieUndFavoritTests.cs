using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

public class KategorieUndFavoritTests
{
    private static async Task<(ENote.WebApp.Data.ApplicationDbContext Db, Salon Salon, AppUser User)> BasisAsync(
        IDbContextFactory<ENote.WebApp.Data.ApplicationDbContext> factory)
    {
        var db = await factory.CreateDbContextAsync();
        var salon = new Salon { Name = "Läden A", IstFreigegeben = true };
        var user = new AppUser { UserName = "u@x.de", Email = "u@x.de", FullName = "User" };
        db.Salons.Add(salon);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (db, salon, user);
    }

    [Fact]
    public async Task SpeichereKategorie_Legt_An_Und_Vermeidet_Duplikate()
    {
        var factory = TestDbContextFactory.Create();
        var (_, salon, _) = await BasisAsync(factory);
        var service = ErstelleKatalogService(factory);

        var k1 = await service.SpeichereKategorieAsync(salon.Id, "Tissus");
        var k2 = await service.SpeichereKategorieAsync(salon.Id, "Tissus"); // Duplikat
        var k3 = await service.SpeichereKategorieAsync(salon.Id, "Boissons");

        var alle = await service.GetKategorienAsync(salon.Id);
        Assert.Equal(2, alle.Count); // kein Duplikat
        Assert.Equal(k1.Id, k2.Id);
    }

    [Fact]
    public async Task ToggleFavorit_Fuegt_Hinzu_Und_Entfernt()
    {
        var factory = TestDbContextFactory.Create();
        var (db, salon, user) = await BasisAsync(factory);
        var service = ErstelleKatalogService(factory);

        var leistung = new Leistung { SalonId = salon.Id, Name = "Coupe", Preis = 3000, IstAktiv = true };
        db.Leistungen.Add(leistung);
        await db.SaveChangesAsync();

        // Hinzufügen
        await service.ToggleFavoritAsync(salon.Id, leistung.Id, user.Id);
        var favoriten = await service.GetFavoritenIdsAsync(salon.Id);
        Assert.Contains(leistung.Id, favoriten);

        // Entfernen
        await service.ToggleFavoritAsync(salon.Id, leistung.Id, user.Id);
        favoriten = await service.GetFavoritenIdsAsync(salon.Id);
        Assert.DoesNotContain(leistung.Id, favoriten);
    }

    [Fact]
    public async Task Favoriten_Sind_AppWeit_Pro_Salon()
    {
        // Favorit, den ein Kassierer setzt, ist für alle im selben Laden sichtbar.
        var factory = TestDbContextFactory.Create();
        var (db, salon, user) = await BasisAsync(factory);
        var service = ErstelleKatalogService(factory);

        var leistung = new Leistung { SalonId = salon.Id, Name = "Tissu A", Preis = 1000, IstAktiv = true };
        db.Leistungen.Add(leistung);
        await db.SaveChangesAsync();

        await service.ToggleFavoritAsync(salon.Id, leistung.Id, user.Id);

        // Eine andere Abfrage (z. B. anderer Nutzer im selben Laden) sieht denselben Favoriten.
        var favoriten = await service.GetFavoritenIdsAsync(salon.Id);
        Assert.Contains(leistung.Id, favoriten);
    }

    [Fact]
    public async Task BenenneKategorieUm_Aendert_Name_Leistungen_Bleiben_Verknuepft()
    {
        var factory = TestDbContextFactory.Create();
        var (db, salon, _) = await BasisAsync(factory);
        var service = ErstelleKatalogService(factory);

        var kategorie = await service.SpeichereKategorieAsync(salon.Id, "Tissu");
        var leistung = new Leistung { SalonId = salon.Id, Name = "Tissu A", Preis = 1000, IstAktiv = true, KategorieId = kategorie.Id };
        db.Leistungen.Add(leistung);
        await db.SaveChangesAsync();

        var (erfolg, fehler) = await service.BenenneKategorieUmAsync(kategorie.Id, salon.Id, "Tissus");
        Assert.True(erfolg, fehler);

        await using var pruefDb = await factory.CreateDbContextAsync();
        var leistungNeu = await pruefDb.Leistungen.Include(l => l.Kategorie).FirstAsync(l => l.Id == leistung.Id);
        Assert.Equal("Tissus", leistungNeu.Kategorie!.Name); // Verknüpfung bleibt, Name aktualisiert
    }

    [Fact]
    public async Task BenenneKategorieUm_Lehnt_Duplikat_Ab()
    {
        var factory = TestDbContextFactory.Create();
        var (_, salon, _) = await BasisAsync(factory);
        var service = ErstelleKatalogService(factory);

        await service.SpeichereKategorieAsync(salon.Id, "Tissus");
        var boissons = await service.SpeichereKategorieAsync(salon.Id, "Boissons");

        var (erfolg, fehler) = await service.BenenneKategorieUmAsync(boissons.Id, salon.Id, "Tissus");
        Assert.False(erfolg);
        Assert.NotNull(fehler);
    }

    [Fact]
    public async Task LoescheKategorie_Entfernt_Kategorie_Leistungen_Bleiben()
    {
        var factory = TestDbContextFactory.Create();
        var (db, salon, _) = await BasisAsync(factory);
        var service = ErstelleKatalogService(factory);

        var kategorie = await service.SpeichereKategorieAsync(salon.Id, "Tissus");
        var leistung = new Leistung { SalonId = salon.Id, Name = "Tissu A", Preis = 1000, IstAktiv = true, KategorieId = kategorie.Id };
        db.Leistungen.Add(leistung);
        await db.SaveChangesAsync();

        await service.LoescheKategorieAsync(kategorie.Id, salon.Id);

        await using var pruefDb = await factory.CreateDbContextAsync();
        var leistungNeu = await pruefDb.Leistungen.FirstAsync(l => l.Id == leistung.Id);
        Assert.Null(leistungNeu.KategorieId); // Leistung bleibt, Kategorie entfernt
        Assert.Empty(await pruefDb.LeistungKategorien.Where(k => k.SalonId == salon.Id).ToListAsync());
    }

    private static KatalogService ErstelleKatalogService(IDbContextFactory<ENote.WebApp.Data.ApplicationDbContext> factory)
        => new(factory, userManager: null!, mediaStorage: null!);
}
