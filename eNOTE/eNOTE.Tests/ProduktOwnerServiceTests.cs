using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ENote.Tests;

public class ProduktOwnerServiceTests
{
    [Fact]
    public async Task WeiseSalonZu_Erstellt_Zuweisung_Mit_Standardrechten()
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var po = new AppUser { UserName = "po@x.de", Email = "po@x.de", FullName = "Pro Owner" };
        var salon = new Salon { Name = "Läden A", IstFreigegeben = true };
        db.Users.Add(po);
        db.Salons.Add(salon);
        await db.SaveChangesAsync();

        var service = new ProduktOwnerService(factory);
        await service.WeiseSalonZuAsync(po.Id, salon.Id, kannLadenUmsatz: true, kannPlattformUmsatz: false);

        var zuweisungen = await service.GetZuweisungenAsync(po.Id);
        Assert.Single(zuweisungen);
        Assert.True(zuweisungen[0].KannLadenUmsatzSehen);
        Assert.False(zuweisungen[0].KannPlattformUmsatzSehen);
    }

    [Fact]
    public async Task WeiseSalonZu_Zweimal_Aktualisiert_Statt_Duplikat()
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var po = new AppUser { UserName = "po@x.de", Email = "po@x.de", FullName = "Pro Owner" };
        var salon = new Salon { Name = "Läden A", IstFreigegeben = true };
        db.Users.Add(po);
        db.Salons.Add(salon);
        await db.SaveChangesAsync();

        var service = new ProduktOwnerService(factory);
        await service.WeiseSalonZuAsync(po.Id, salon.Id, kannLadenUmsatz: true, kannPlattformUmsatz: false);
        await service.WeiseSalonZuAsync(po.Id, salon.Id, kannLadenUmsatz: false, kannPlattformUmsatz: true);

        var zuweisungen = await service.GetZuweisungenAsync(po.Id);
        Assert.Single(zuweisungen); // kein Duplikat
        Assert.False(zuweisungen[0].KannLadenUmsatzSehen);
        Assert.True(zuweisungen[0].KannPlattformUmsatzSehen);
    }

    [Fact]
    public async Task GetVerwalteteSalonIds_Nur_Zugewiesene()
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var po = new AppUser { UserName = "po@x.de", Email = "po@x.de", FullName = "Pro Owner" };
        var salonA = new Salon { Name = "Läden A", IstFreigegeben = true };
        var salonB = new Salon { Name = "Läden B", IstFreigegeben = true };
        db.Users.Add(po);
        db.Salons.AddRange(salonA, salonB);
        await db.SaveChangesAsync();

        var service = new ProduktOwnerService(factory);
        await service.WeiseSalonZuAsync(po.Id, salonA.Id, true, false);

        var ids = await service.GetVerwalteteSalonIdsAsync(po.Id);
        Assert.Contains(salonA.Id, ids);
        Assert.DoesNotContain(salonB.Id, ids); // nicht zugewiesen → nicht sichtbar
    }

    [Fact]
    public async Task EntferneZuweisung_Entfernt_Salon()
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var po = new AppUser { UserName = "po@x.de", Email = "po@x.de", FullName = "Pro Owner" };
        var salon = new Salon { Name = "Läden A", IstFreigegeben = true };
        db.Users.Add(po);
        db.Salons.Add(salon);
        await db.SaveChangesAsync();

        var service = new ProduktOwnerService(factory);
        await service.WeiseSalonZuAsync(po.Id, salon.Id, true, false);
        var z = (await service.GetZuweisungenAsync(po.Id)).Single();

        await service.EntferneZuweisungAsync(z.ZuweisungId);
        Assert.Empty(await service.GetZuweisungenAsync(po.Id));
    }

    [Fact]
    public async Task SetzeProvision_Speichert_Prozent_Und_Festbetrag()
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var po = new AppUser { UserName = "po@x.de", Email = "po@x.de", FullName = "Pro Owner" };
        var salon = new Salon { Name = "Läden A", IstFreigegeben = true };
        db.Users.Add(po);
        db.Salons.Add(salon);
        await db.SaveChangesAsync();

        var service = new ProduktOwnerService(factory);
        await service.WeiseSalonZuAsync(po.Id, salon.Id, true, false);
        var z = (await service.GetZuweisungenAsync(po.Id)).Single();

        await service.SetzeProvisionAsync(z.ZuweisungId, prozent: 10m, festbetrag: 500m);
        var aktualisiert = (await service.GetZuweisungenAsync(po.Id)).Single();
        Assert.Equal(10m, aktualisiert.ProzentProvision);
        Assert.Equal(500m, aktualisiert.FestbetragProvision);
    }

    [Fact]
    public async Task SetzeProvision_Lehnt_Ungueltige_Werte_Ab()
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var po = new AppUser { UserName = "po@x.de", Email = "po@x.de", FullName = "Pro Owner" };
        var salon = new Salon { Name = "Läden A", IstFreigegeben = true };
        db.Users.Add(po);
        db.Salons.Add(salon);
        await db.SaveChangesAsync();

        var service = new ProduktOwnerService(factory);
        await service.WeiseSalonZuAsync(po.Id, salon.Id, true, false);
        var z = (await service.GetZuweisungenAsync(po.Id)).Single();

        Assert.False(await service.SetzeProvisionAsync(z.ZuweisungId, prozent: 150m, festbetrag: null)); // > 100 %
        Assert.False(await service.SetzeProvisionAsync(z.ZuweisungId, prozent: null, festbetrag: -100m)); // negativ
    }

    [Fact]
    public async Task WeiseSalonZu_Ein_Laden_Hat_Genau_Einen_Po_Admin_Kann_Wechseln()
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var poA = new AppUser { UserName = "poa", Email = "a@x.de", FullName = "PO A" };
        var poB = new AppUser { UserName = "pob", Email = "b@x.de", FullName = "PO B" };
        var salonA = new Salon { Name = "Läden A", IstFreigegeben = true };
        var salonB = new Salon { Name = "Läden B", IstFreigegeben = true };
        db.Users.AddRange(poA, poB);
        db.Salons.AddRange(salonA, salonB);
        await db.SaveChangesAsync();

        var service = new ProduktOwnerService(factory);
        await service.WeiseSalonZuAsync(poA.Id, salonA.Id, true, false);
        // Ein PO kann mehrere Läden verwalten.
        await service.WeiseSalonZuAsync(poA.Id, salonB.Id, true, false);
        Assert.Equal(2, (await service.GetZuweisungenAsync(poA.Id)).Count);

        // Admin wechselt den PO von Laden A: bisherige aktive Zuordnung wird archiviert.
        await service.WeiseSalonZuAsync(poB.Id, salonA.Id, true, false);

        // Aktive Zuordnungen: A hat nur noch Salon B, B hat Salon A.
        Assert.Single(await service.GetZuweisungenAsync(poA.Id)); // nur noch Salon B
        Assert.Equal(salonB.Id, (await service.GetZuweisungenAsync(poA.Id))[0].SalonId);
        Assert.Single(await service.GetZuweisungenAsync(poB.Id)); // Salon A
        Assert.Equal(salonA.Id, (await service.GetZuweisungenAsync(poB.Id))[0].SalonId);
        Assert.DoesNotContain(salonA.Id, await service.GetVerwalteteSalonIdsAsync(poA.Id));
    }

    [Fact]
    public async Task LegeSalonSelbstAn_Legt_Besitzer_Laden_Und_Direkte_Verwaltung_An()
    {
        var factory = TestDbContextFactory.Create();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddSingleton(factory);
        services.AddTransient(_ => factory.CreateDbContext());
        services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        services.AddIdentityCore<AppUser>().AddRoles<Role>().AddEntityFrameworkStores<ENote.WebApp.Data.ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        var userManager = provider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        var roleManager = provider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Role>>();
        Assert.True((await roleManager.CreateAsync(new Role { Name = AppRollen.Besitzer })).Succeeded);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser { UserName = "po", Email = "po@x.de", FullName = "PO" });
            await db.SaveChangesAsync();
        }
        Guid poId;
        await using (var db = await factory.CreateDbContextAsync())
            poId = (await db.Users.SingleAsync()).Id;

        var service = new ProduktOwnerService(factory);
        var (erfolg, fehler, salon) = await service.LegeSalonSelbstAnAsync(
            poId, "Jean", "Test", "jean.boutique", "kontakt@x.de", "Init123!Ab", "Boutique A", "Lomé", "Togo", userManager, telefon: "+228 90 12 34 56");

        Assert.True(erfolg, fehler);
        Assert.NotNull(salon);
        // PO verwaltet den Laden direkt (SelbstAngelegt).
        var zuweisung = Assert.Single(await service.GetZuweisungenAsync(poId));
        Assert.True(zuweisung.SelbstAngelegt);
        Assert.Equal(salon!.Id, zuweisung.SalonId);
        // Neues Besitzer-Konto mit Pseudonym als Login wurde angelegt und dem Laden zugeordnet.
        var besitzer = await userManager.FindByNameAsync("jean.boutique");
        Assert.NotNull(besitzer);
        Assert.True(await userManager.IsInRoleAsync(besitzer, AppRollen.Besitzer));
        await using var kontrolle = await factory.CreateDbContextAsync();
        Assert.Equal(besitzer.Id, (await kontrolle.UserSalons.SingleAsync(us => us.SalonId == salon.Id)).AppUserId);
    }
}
