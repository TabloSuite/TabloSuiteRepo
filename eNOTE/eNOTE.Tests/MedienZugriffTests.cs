using System.Security.Claims;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Infrastruktur;
using ENote.Core.DataModels.Finanzen;
using ENote.Core.DataModels.Salons;
using ENote.Core.DataModels.Katalog;
using ENote.Core.Services.Storage;
using ENote.WebApp.Services;
using ENote.WebApp.Services.Storage;
using Xunit;

namespace ENote.Tests;

public class MedienZugriffTests
{
    [Fact]
    public async Task Nur_Registrierte_Medien_Des_Eigenen_Aktiven_Ladens()
    {
        var factory = TestDbContextFactory.Create();
        var user = new AppUser { Id = Guid.NewGuid() };
        var salon = new Salon { Name = "A", IstAktiv = true };
        var asset = new BlobMediaAsset { SalonId = salon.Id, BlobName = MedienDateien.NeuerName(salon.Id, "photo.png") };
        await using var db = await factory.CreateDbContextAsync();
        db.UserSalons.Add(new UserSalon { AppUser = user, Salon = salon });
        db.BlobMediaAssets.Add(asset); await db.SaveChangesAsync();
        var zugriff = new MedienZugriff(factory);
        Assert.NotNull(await zugriff.LadeAssetAsync(Principal(user.Id), asset.BlobName));
        Assert.Null(await zugriff.LadeAssetAsync(new ClaimsPrincipal(), asset.BlobName));
        Assert.Null(await zugriff.LadeAssetAsync(Principal(Guid.NewGuid()), asset.BlobName));
        Assert.Null(await zugriff.LadeAssetAsync(Principal(user.Id), MedienDateien.NeuerName(salon.Id, "other.png")));
        salon.IstAktiv = false; await db.SaveChangesAsync();
        Assert.Null(await zugriff.LadeAssetAsync(Principal(user.Id), asset.BlobName));
        salon.IstAktiv = true; user.IsBlocked = true; await db.SaveChangesAsync();
        Assert.Null(await zugriff.LadeAssetAsync(Principal(user.Id), asset.BlobName));
    }

    [Fact]
    public async Task BlobPrefix_Und_UploadEigentuemer_Ersetzen_Keine_Mandantenrechte()
    {
        var factory = TestDbContextFactory.Create(); var user = new AppUser { Id = Guid.NewGuid() };
        var a = new Salon { Name = "A" }; var b = new Salon { Name = "B" };
        var asset = new BlobMediaAsset { SalonId = b.Id, HochgeladenVonId = user.Id, BlobName = MedienDateien.NeuerName(a.Id, "image.png") };
        await using var db = await factory.CreateDbContextAsync();
        db.UserSalons.Add(new UserSalon { AppUser = user, Salon = a }); db.Salons.Add(b); db.BlobMediaAssets.Add(asset);
        await db.SaveChangesAsync();
        Assert.Null(await new MedienZugriff(factory).LadeAssetAsync(Principal(user.Id), asset.BlobName));
    }

    [Fact]
    public async Task Belege_Benoetigen_Finanzrecht_Bons_Nur_Ladenzugang()
    {
        var factory = TestDbContextFactory.Create(); var user = new AppUser { Id = Guid.NewGuid() };
        var salon = new Salon { Name = "A" };
        var zuordnung = new UserSalon { AppUser = user, Salon = salon };
        var asset = new BlobMediaAsset { SalonId = salon.Id, BlobName = MedienDateien.NeuerName(salon.Id, "receipt.pdf") };
        await using var db = await factory.CreateDbContextAsync();
        db.UserSalons.Add(zuordnung); db.BlobMediaAssets.Add(asset); db.Ausgaben.Add(new Ausgabe { SalonId = salon.Id, BelegAssetId = asset.Id });
        await db.SaveChangesAsync(); var zugriff = new MedienZugriff(factory);
        Assert.True(await zugriff.HatSalonZugangAsync(Principal(user.Id), salon.Id));
        Assert.Null(await zugriff.LadeAssetAsync(Principal(user.Id), asset.BlobName));
        zuordnung.KannFinanzenSehen = true; await db.SaveChangesAsync();
        Assert.NotNull(await zugriff.LadeAssetAsync(Principal(user.Id), asset.BlobName));
    }

    [Fact]
    public async Task ProduktOwner_Nur_Zugewiesene_Laeden_SuperAdmin_Global_Aber_Nicht_Gesperrt()
    {
        var factory = TestDbContextFactory.Create(); var po = new AppUser { Id = Guid.NewGuid() }; var admin = new AppUser { Id = Guid.NewGuid() };
        var salon = new Salon { Name = "A" };
        var asset = new BlobMediaAsset { SalonId = salon.Id, BlobName = MedienDateien.NeuerName(salon.Id, "photo.png") };
        var global = new BlobMediaAsset { BlobName = MedienDateien.NeuerName(null, "global.png") };
        var poRolle = new Role { Name = AppRollen.ProduktOwner }; var adminRolle = new Role { Name = AppRollen.SuperAdmin };
        await using var db = await factory.CreateDbContextAsync();
        db.Users.AddRange(po, admin); db.Roles.AddRange(poRolle, adminRolle);
        db.UserRoles.AddRange(new AppUserRole { UserId = po.Id, RoleId = poRolle.Id }, new AppUserRole { UserId = admin.Id, RoleId = adminRolle.Id });
        db.Salons.Add(salon); db.BlobMediaAssets.AddRange(asset, global); await db.SaveChangesAsync();
        var zugriff = new MedienZugriff(factory);
        Assert.Null(await zugriff.LadeAssetAsync(Principal(po.Id), asset.BlobName));
        db.ProduktOwnerSalons.Add(new ProduktOwnerSalon { ProduktOwnerId = po.Id, SalonId = salon.Id }); await db.SaveChangesAsync();
        Assert.NotNull(await zugriff.LadeAssetAsync(Principal(po.Id), asset.BlobName));
        Assert.Null(await zugriff.LadeAssetAsync(Principal(po.Id), global.BlobName));
        Assert.NotNull(await zugriff.LadeAssetAsync(Principal(admin.Id), global.BlobName));
        admin.IsBlocked = true; await db.SaveChangesAsync();
        Assert.Null(await zugriff.LadeAssetAsync(Principal(admin.Id), global.BlobName));
    }

    [Fact]
    public async Task Uploads_Pruefen_Laden_Und_Bearbeitungsrechte_Vor_Storage()
    {
        var factory = TestDbContextFactory.Create(); var user = new AppUser { Id = Guid.NewGuid() };
        var salon = new Salon { Name = "A" }; var zuordnung = new UserSalon { AppUser = user, Salon = salon };
        var leistung = new Leistung { SalonId = salon.Id, Name = "Article" };
        var kategorie = new AusgabenKategorie { SalonId = salon.Id, Name = "Divers", IstAktiv = true };
        await using var db = await factory.CreateDbContextAsync();
        db.UserSalons.Add(zuordnung); db.Leistungen.Add(leistung); db.AusgabenKategorien.Add(kategorie); await db.SaveChangesAsync();
        var storage = new UploadZaehler();
        var katalog = new KatalogService(factory, null!, storage);
        var salone = new SalonService(factory, storage); var ausgaben = new AusgabenService(factory, storage);
        using var inhalt = new MemoryStream([1]);
        Assert.False((await salone.SetzeLogoAsync(salon.Id, "logo.png", "image/png", inhalt, user.Id)).Erfolg);
        Assert.False((await katalog.SpeichereLeistungsBildAsync(leistung.Id, "photo.png", "image/png", inhalt, user.Id)).Erfolg);
        Assert.False((await ausgaben.ErfasseAusgabeAsync(salon.Id, kategorie.Id, 1, DateTime.UtcNow, null, user.Id, ("receipt.pdf", "application/pdf", inhalt))).Erfolg);
        Assert.Equal(0, storage.Anzahl);
        zuordnung.KannLeistungenVerwalten = true; zuordnung.KannFinanzenSehen = true; await db.SaveChangesAsync();
        Assert.True((await katalog.SpeichereLeistungsBildAsync(leistung.Id, "photo.png", "image/png", inhalt, user.Id)).Erfolg);
        Assert.True((await ausgaben.ErfasseAusgabeAsync(salon.Id, kategorie.Id, 1, DateTime.UtcNow, null, user.Id, ("receipt.pdf", "application/pdf", inhalt))).Erfolg);
        Assert.Equal(2, storage.Anzahl);
        Assert.False((await salone.SetzeLogoAsync(salon.Id, "logo.png", "image/png", inhalt, user.Id)).Erfolg);
        var rolle = new Role { Name = AppRollen.Besitzer }; db.Roles.Add(rolle);
        db.UserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = rolle.Id }); await db.SaveChangesAsync();
        Assert.True((await salone.SetzeLogoAsync(salon.Id, "logo.png", "image/png", inhalt, user.Id)).Erfolg);
        user.IsBlocked = true; await db.SaveChangesAsync();
        Assert.False((await katalog.SpeichereLeistungsBildAsync(leistung.Id, "photo.png", "image/png", inhalt, user.Id)).Erfolg);
        Assert.Equal(3, storage.Anzahl);
    }

    private sealed class UploadZaehler : IMediaStorageService
    {
        public int Anzahl { get; private set; }
        public Task<MediaSaveResult> SaveAsync(MediaSaveRequest request, CancellationToken cancellationToken = default)
        {
            Anzahl++; var name = MedienDateien.NeuerName(request.SalonId, request.DateiName);
            return Task.FromResult(MediaSaveResult.Ok(name, MedienDateien.Url(name), 1));
        }
        public string GetPublicUrl(string blobName) => MedienDateien.Url(blobName);
        public Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]?> LadeBytesAsync(string blobName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static ClaimsPrincipal Principal(Guid id)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Tests"));
}
