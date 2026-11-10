using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

public class VertragsServiceTests
{
    [Fact]
    public async Task Zahlungsempfaenger_Prueft_Ladenzugriff_Und_Nur_Aktive_Betreuung()
    {
        var factory = TestDbContextFactory.Create();
        var owner = new AppUser { Id = Guid.NewGuid() };
        var po = new AppUser { Id = Guid.NewGuid(), FullName = "Responsable A" };
        var salon = new Salon { Name = "A" };
        var fremd = new Salon { Name = "B" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.Besitzer };
            db.Roles.Add(rolle);
            db.UserRoles.Add(new AppUserRole { UserId = owner.Id, RoleId = rolle.Id });
            db.UserSalons.Add(new UserSalon { AppUser = owner, Salon = salon });
            db.Users.Add(po); db.Salons.Add(fremd);
            db.BesitzerBetreuungen.Add(new BesitzerBetreuung { BesitzerId = owner.Id, ProduktOwnerId = po.Id, GueltigBisUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var service = new VertragsService(factory, TimeProvider.System);
        Assert.Null(await service.GetZahlungsempfaengerAsync(owner.Id, salon.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetZahlungsempfaengerAsync(owner.Id, fremd.Id));
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.BesitzerBetreuungen.Add(new BesitzerBetreuung { BesitzerId = owner.Id, ProduktOwnerId = po.Id });
            await db.SaveChangesAsync();
        }
        Assert.Equal("Responsable A", await service.GetZahlungsempfaengerAsync(owner.Id, salon.Id));
        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.Users.SingleAsync(u => u.Id == owner.Id)).IsBlocked = true;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetZahlungsempfaengerAsync(owner.Id, salon.Id));
    }

    private class Uhr(DateTime jetzt) : TimeProvider
    {
        public DateTime Jetzt = jetzt;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData(AboIntervall.Monatlich, 1, "Mensuel")]
    [InlineData(AboIntervall.DreiMonate, 3, "Trimestriel")]
    [InlineData(AboIntervall.SechsMonate, 6, "Semestriel")]
    [InlineData(AboIntervall.Jaehrlich, 12, "Annuel")]
    public void Laufzeiten(AboIntervall intervall, int monate, string name)
    {
        Assert.Equal(monate, VertragsRegeln.Monate(intervall));
        Assert.Equal(name, VertragsRegeln.Bezeichnung(intervall));
    }

    [Fact]
    public async Task Probezeit_Automatik_Wechsel_Und_Zahlung_Sind_Getrennt_Und_Idempotent()
    {
        var factory = TestDbContextFactory.Create();
        var ende = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var uhr = new Uhr(ende.AddDays(-1));
        var salon = new Salon { Name = "A", IstFreigegeben = true, ProbeBisUtc = ende };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Besitzer" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.Besitzer };
            db.Roles.Add(rolle);
            db.UserRoles.Add(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id });
            db.UserSalons.Add(new UserSalon { AppUser = besitzer, Salon = salon });
            db.PlattformPreise.Add(new PlattformPreis { Intervall = AboIntervall.Monatlich, Preis = 5000 });
            await db.SaveChangesAsync();
        }
        var service = new VertragsService(factory, uhr);
        await service.SynchronisiereAsync(salon.Id);
        await using (var db = await factory.CreateDbContextAsync()) Assert.Empty(await db.NutzungsAbos.ToListAsync());
        uhr.Jetzt = ende;
        await service.SynchronisiereAsync(salon.Id);
        await service.SynchronisiereAsync(salon.Id);
        Guid aboId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var abo = Assert.Single(await db.NutzungsAbos.ToListAsync());
            aboId = abo.Id;
            Assert.Equal(ende, abo.ZeitraumVonUtc);
            Assert.Equal(ende.AddMonths(1), abo.ZeitraumBisUtc);
            Assert.Equal(AboIntervall.Monatlich, abo.Intervall);
            Assert.Equal(AboZahlungStatus.Offen, abo.ZahlungStatus);
            Assert.Equal(5000, abo.PreisSnapshot);
        }
        // Zahlung bereits gemeldet → Wechsel gilt ab der nächsten Periode (keine Sofort-Umwandlung).
        await service.MeldeAsync(besitzer.Id, aboId, "reference");
        Assert.Equal(VertragsService.WechselArt.AbNaechsterPeriode, await service.WechsleAsync(besitzer.Id, salon.Id, AboIntervall.SechsMonate));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MeldeAsync(besitzer.Id, aboId, null));
        uhr.Jetzt = ende.AddMonths(1);
        await service.SynchronisiereAsync(salon.Id);
        await service.SynchronisiereAsync(salon.Id);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var abos = await db.NutzungsAbos.OrderBy(a => a.ZeitraumVonUtc).ToListAsync();
            Assert.Equal(2, abos.Count);
            Assert.Equal(AboZahlungStatus.Gemeldet, abos[0].ZahlungStatus);
            Assert.Equal(AboIntervall.SechsMonate, abos[1].Intervall);
            Assert.Equal(27750, abos[1].PreisSnapshot);
            Assert.Equal(ende.AddMonths(7), abos[1].ZeitraumBisUtc);
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.WechsleAsync(Guid.NewGuid(), salon.Id, AboIntervall.Jaehrlich));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.MeldeAsync(Guid.NewGuid(), aboId, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetZahlungenAsync(Guid.NewGuid(), ende, ende));
        var zahlungen = await service.GetZahlungenAsync(besitzer.Id, ende.AddDays(10), ende.AddDays(10));
        Assert.Equal(aboId, Assert.Single(zahlungen).Id);
    }

    [Fact]
    public async Task Ohne_Probezeit_Keine_Rueckwirkende_Forderung_Und_Individueller_Preis_Gilt_Alle_Laufzeiten()
    {
        var factory = TestDbContextFactory.Create();
        var jetzt = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        var salon = new Salon { Name = "A", IstFreigegeben = true, CreatedAtUtc = jetzt.AddYears(-3), IndividuellerMonatsPreis = 1000 };
        await using (var db = await factory.CreateDbContextAsync()) { db.Salons.Add(salon); await db.SaveChangesAsync(); }
        await new VertragsService(factory, new Uhr(jetzt)).SynchronisiereAsync(salon.Id);
        await using var kontrolle = await factory.CreateDbContextAsync();
        Assert.Equal(jetzt, Assert.Single(await kontrolle.NutzungsAbos.ToListAsync()).ZeitraumVonUtc);
        foreach (var intervall in VertragsRegeln.Intervalle)
            Assert.Equal(VertragsRegeln.Preis(1000, intervall), await VertragsService.PreisAsync(kontrolle, salon.Id, intervall, default));
    }

    [Fact]
    public async Task ProduktOwner_Sieht_Nur_Zugewiesene_Perioden_Und_Darf_Nicht_Bestaetigen()
    {
        var factory = TestDbContextFactory.Create();
        var po = new AppUser { Id = Guid.NewGuid(), FullName = "PO" };
        var a = new Salon { Name = "A", IstFreigegeben = false };
        var b = new Salon { Name = "B", IstFreigegeben = false };
        var tag = new DateTime(2026, 11, 1);
        var abo = new NutzungsAbo { Salon = a, ZeitraumVonUtc = tag, ZeitraumBisUtc = tag.AddMonths(1), ZahlungStatus = AboZahlungStatus.Gemeldet };
        await using (var db = await factory.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.ProduktOwner };
            db.Users.Add(po); db.Roles.Add(rolle);
            db.UserRoles.Add(new AppUserRole { UserId = po.Id, RoleId = rolle.Id });
            db.ProduktOwnerSalons.Add(new ProduktOwnerSalon { ProduktOwnerId = po.Id, Salon = a });
            db.NutzungsAbos.AddRange(abo, new NutzungsAbo { Salon = b, ZeitraumVonUtc = tag, ZeitraumBisUtc = tag.AddMonths(1) });
            await db.SaveChangesAsync();
        }
        var daten = await new VertragsService(factory, new Uhr(tag)).GetZahlungenAsync(po.Id, tag, tag);
        Assert.Equal(a.Id, Assert.Single(daten).SalonId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new AboService(factory)
            .SetzeZahlungsStatusAsync(po.Id, abo.Id, AboZahlungStatus.Bestaetigt));
    }
}
