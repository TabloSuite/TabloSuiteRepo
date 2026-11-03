using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
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
}
