using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Infrastruktur;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Wachsende Listen: Suche läuft in der Datenbank, geladen wird seitenweise (« Afficher plus »),
/// Kennzahlen zählen über ALLE Einträge – auch nach Jahren mit tausenden Datensätzen.
/// </summary>
public class ListenSeitenweiseTests
{
    [Fact]
    public async Task Laeden_seitenweise_mit_Suche_nach_Besitzer_und_Kennzahlen_ueber_alle()
    {
        var factory = TestDbContextFactory.Create();
        Guid admin;
        await using (var db = await factory.CreateDbContextAsync())
        {
            admin = LadenRechteTests.LegeKontoAn(db, "admin", AppRollen.SuperAdmin);
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (var i = 0; i < 120; i++)
                db.Salons.Add(new Salon { Name = $"Boutique {i:000}", IstAktiv = i % 10 != 0, IstFreigegeben = i % 4 != 0, CreatedAtUtc = start.AddHours(i) });
            var besonders = new Salon { Name = "Chez Afi", IstAktiv = true, IstFreigegeben = true, CreatedAtUtc = start };
            db.Salons.Add(besonders);
            var besitzer = LadenRechteTests.LegeKontoAn(db, "afi.mensah", AppRollen.Besitzer);
            db.UserSalons.Add(new UserSalon { AppUserId = besitzer, SalonId = besonders.Id });
            await db.SaveChangesAsync();
        }
        var dienst = new AdminService(factory, null!, NullLogger<AdminService>.Instance, null!);

        var (seite1, mehr1) = await dienst.SucheSalonsAsync(admin, true, null, null, null, 0);
        Assert.Equal(50, seite1.Count);
        Assert.True(mehr1);
        Assert.Equal("Boutique 119", seite1[0].Name);   // neueste zuerst

        var (letzte, mehr3) = await dienst.SucheSalonsAsync(admin, true, null, null, null, 100);
        Assert.Equal(21, letzte.Count);
        Assert.False(mehr3);

        var (treffer, _) = await dienst.SucheSalonsAsync(admin, true, "afi.mensah@", null, null, 0);
        Assert.Equal("Chez Afi", Assert.Single(treffer).Name);

        var k = await dienst.GetSalonKennzahlenAsync(admin, true);
        Assert.Equal(121, k.Gesamt);
        Assert.Equal(12, k.Deaktiviert);
        Assert.Equal(k.Gesamt - k.Deaktiviert, k.Probe + k.Zahlend);
    }

    [Fact]
    public async Task Facturation_Summen_zaehlen_auch_ueber_100_Rechnungen()
    {
        var factory = TestDbContextFactory.Create();
        Guid admin;
        await using (var db = await factory.CreateDbContextAsync())
        {
            admin = LadenRechteTests.LegeKontoAn(db, "admin", AppRollen.SuperAdmin);
            var salon = new Salon { Name = "Boutique" };
            db.Salons.Add(salon);
            for (var i = 0; i < 130; i++)
                db.NutzungsAbos.Add(new NutzungsAbo { SalonId = salon.Id, PreisSnapshot = 1000m, ZahlungStatus = AboZahlungStatus.Offen,
                    ZeitraumVonUtc = new DateTime(2026, 1, 1), ZeitraumBisUtc = new DateTime(2026, 2, 1) });
            await db.SaveChangesAsync();
        }
        var dienst = new AboService(factory);

        var k = await dienst.GetKennzahlenAsync(admin, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(130_000m, k.ZuZahlen);   // vorher nur aus den ersten 100 Rechnungen
        Assert.Equal(130, k.Rechnungen);

        var (seite, mehr) = await dienst.SucheAbosAsync(admin, "Bout", AboZahlungStatus.Offen, 0);
        Assert.Equal(50, seite.Count);
        Assert.True(mehr);
    }

    [Fact]
    public async Task Fehlerprotokoll_sucht_in_der_Datenbank_und_laedt_seitenweise()
    {
        var factory = TestDbContextFactory.Create();
        await using (var db = await factory.CreateDbContextAsync())
        {
            for (var i = 0; i < 70; i++)
                db.ExceptionLogs.Add(new ExceptionLog { Nachricht = i == 5 ? "Timeout Postmark" : $"Fehler {i}", ZeitUtc = DateTime.UtcNow.AddMinutes(-i) });
            await db.SaveChangesAsync();
        }
        var dienst = new ExceptionLogService(factory);

        var (seite, mehr) = await dienst.SucheLogsAsync(null, null, false, null, 0);
        Assert.Equal(50, seite.Count);
        Assert.True(mehr);
        var (treffer, _) = await dienst.SucheLogsAsync(null, null, false, "Postmark", 0);
        Assert.Equal("Timeout Postmark", Assert.Single(treffer).Nachricht);
    }
}
