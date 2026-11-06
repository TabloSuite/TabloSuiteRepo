using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Finanzen;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

public class BesitzerFinanzServiceTests
{
    [Fact]
    public void Vertragsart_Ist_Unabhaengig_Von_Zahlung_Und_Standard_Ist_Monatlich()
    {
        var jetzt = new DateTime(2026, 11, 10);
        var abo = new NutzungsAbo { Intervall = AboIntervall.Jaehrlich, ZeitraumVonUtc = jetzt.AddDays(-1), ZeitraumBisUtc = jetzt.AddYears(1), ZahlungStatus = AboZahlungStatus.Gemeldet };
        Assert.Equal("Essai gratuit", BesitzerFinanzService.VertragsText([abo], jetzt.AddDays(1), jetzt));
        Assert.Equal("Contrat actif · Annuel", BesitzerFinanzService.VertragsText([abo], null, jetzt));
        abo.ZahlungStatus = AboZahlungStatus.Bestaetigt;
        Assert.Equal("Essai gratuit", BesitzerFinanzService.VertragsText([abo], jetzt.AddDays(1), jetzt));
        abo.ZeitraumBisUtc = jetzt;
        Assert.Equal("Contrat actif · Mensuel", BesitzerFinanzService.VertragsText([abo], null, jetzt));
        abo.ZeitraumVonUtc = jetzt.AddDays(1);
        abo.ZeitraumBisUtc = jetzt.AddYears(1);
        Assert.Equal("Contrat actif · Mensuel", BesitzerFinanzService.VertragsText([abo], null, jetzt));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Ohne_Aktiven_SuperAdmin_Kein_Zugriff(bool adminrolle, bool gesperrt)
    {
        var factory = TestDbContextFactory.Create();
        var user = new AppUser { IsBlocked = gesperrt, FullName = "Test" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(user);
            var rolle = new Role { Name = adminrolle ? AppRollen.SuperAdmin : AppRollen.ProduktOwner };
            db.Roles.Add(rolle);
            db.UserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = rolle.Id });
            await db.SaveChangesAsync();
        }
        var service = new BesitzerFinanzService(factory);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetBesitzerAsync(user.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetLaedenAsync(user.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetBerichtAsync(user.Id, Guid.NewGuid(), Guid.NewGuid(), DateTime.Today, DateTime.Today));
    }

    [Fact]
    public async Task Bericht_Trennt_Laeden_Zeitraeume_Und_AboEingaenge()
    {
        var factory = TestDbContextFactory.Create();
        var admin = new AppUser { FullName = "Admin" };
        var besitzer = new AppUser { FullName = "Besitzer ohne PIN", Email = "test@example.test" };
        var a = new Salon { Name = "A" };
        var b = new Salon { Name = "B" };
        var fremd = new Salon { Name = "Fremd" };
        var tag = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var adminrolle = new Role { Name = AppRollen.SuperAdmin };
            var besitzerrolle = new Role { Name = AppRollen.Besitzer };
            db.Roles.AddRange(adminrolle, besitzerrolle);
            db.Users.AddRange(admin, besitzer);
            db.UserRoles.AddRange(new AppUserRole { UserId = admin.Id, RoleId = adminrolle.Id }, new AppUserRole { UserId = besitzer.Id, RoleId = besitzerrolle.Id });
            db.UserSalons.AddRange(new UserSalon { AppUser = besitzer, Salon = a }, new UserSalon { AppUser = besitzer, Salon = b });
            db.Salons.Add(fremd);
            var sitzung = new KassenSitzung { Salon = a, Kassierer = besitzer };
            db.KassenSitzungen.Add(sitzung);
            db.Verkaeufe.AddRange(
                new Verkauf { SalonId = a.Id, KassenSitzung = sitzung, ZeitUtc = tag, GesamtSumme = 100, ZahlungsArt = ZahlungsArt.Bar },
                new Verkauf { SalonId = a.Id, KassenSitzung = sitzung, ZeitUtc = tag.AddDays(1).AddTicks(-1), GesamtSumme = 200, ZahlungsArt = ZahlungsArt.Karte },
                new Verkauf { SalonId = a.Id, KassenSitzung = sitzung, ZeitUtc = tag.AddDays(1), GesamtSumme = 1000 },
                new Verkauf { SalonId = b.Id, KassenSitzung = sitzung, ZeitUtc = tag, GesamtSumme = 9999 });
            db.Ausgaben.AddRange(new Ausgabe { SalonId = a.Id, DatumUtc = tag.AddHours(23), Betrag = 40 },
                new Ausgabe { SalonId = b.Id, DatumUtc = tag, Betrag = 9000 });
            db.NutzungsAbos.AddRange(
                new NutzungsAbo { SalonId = a.Id, PreisSnapshot = 5000, ZahlungStatus = AboZahlungStatus.Bestaetigt, ZahlungGemeldetUtc = tag.AddDays(-5), ZahlungBestaetigtUtc = tag.AddHours(23) },
                new NutzungsAbo { SalonId = a.Id, PreisSnapshot = 6000, ZahlungStatus = AboZahlungStatus.Bestaetigt, ZahlungGemeldetUtc = tag, ZahlungBestaetigtUtc = tag.AddDays(1) },
                new NutzungsAbo { SalonId = a.Id, PreisSnapshot = 700, ZahlungStatus = AboZahlungStatus.Gemeldet, ZahlungGemeldetUtc = tag },
                new NutzungsAbo { SalonId = a.Id, PreisSnapshot = 800, ZahlungStatus = AboZahlungStatus.Offen, CreatedAtUtc = tag },
                new NutzungsAbo { SalonId = a.Id, PreisSnapshot = 900, ZahlungStatus = AboZahlungStatus.Abgelehnt, ZahlungGemeldetUtc = tag },
                new NutzungsAbo { SalonId = b.Id, PreisSnapshot = 9999, ZahlungStatus = AboZahlungStatus.Bestaetigt, ZahlungBestaetigtUtc = tag });
            await db.SaveChangesAsync();
        }
        var service = new BesitzerFinanzService(factory);
        Assert.Equal(besitzer.Id, Assert.Single(await service.GetBesitzerAsync(admin.Id)).Id);
        Assert.Equal(2, (await service.GetLaedenAsync(admin.Id, besitzer.Id)).Count);
        var bericht = await service.GetBerichtAsync(admin.Id, besitzer.Id, a.Id, tag, tag);
        var monat = Assert.Single(bericht.Monate);
        Assert.Equal(2, monat.Verkaeufe);
        Assert.Equal(100m, monat.Bar);
        Assert.Equal(200m, monat.Karte);
        Assert.Equal(40m, monat.Ausgaben);
        Assert.Equal(260m, monat.Ergebnis);
        Assert.Equal(5000m, bericht.Bestaetigt);
        Assert.Equal(700m, bericht.Gemeldet);
        Assert.Equal(800m, bericht.Offen);
        Assert.Equal(4, bericht.Abos.Count);
        var leer = await service.GetBerichtAsync(admin.Id, besitzer.Id, a.Id, tag.AddYears(1), tag.AddYears(1));
        Assert.Empty(leer.Monate);
        Assert.Empty(leer.Abos);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetBerichtAsync(admin.Id, besitzer.Id, fremd.Id, tag, tag));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetBerichtAsync(admin.Id, besitzer.Id, a.Id, tag.AddDays(1), tag));
    }
}
