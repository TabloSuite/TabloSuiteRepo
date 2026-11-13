using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

/// <summary>Übergang Probezeit → erstes Abo: erste Rechnung genau am 1. des Folgemonats.</summary>
public class ProbezeitUebergangTests
{
    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public DateTime Jetzt { get; set; } = jetzt;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Registrierung_10_November_Probe_Bis_Ende_Januar_Erste_Rechnung_1_Februar()
    {
        var registriert = new DateTime(2026, 11, 10, 14, 30, 0, DateTimeKind.Utc);
        var probeEnde = AuthService.BerechneProbeEnde(registriert);
        Assert.Equal(new DateTime(2027, 1, 31), probeEnde.Date); // November + Dezember + Januar

        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Name = "Neu", IstAktiv = true, IstFreigegeben = true, ProbeBisUtc = probeEnde };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            db.PlattformPreise.Add(new PlattformPreis { Intervall = AboIntervall.Monatlich, Preis = 5000, IstAktuell = true, GueltigAbUtc = registriert });
            await db.SaveChangesAsync();
        }

        var uhr = new Uhr(new DateTime(2027, 1, 31, 18, 0, 0, DateTimeKind.Utc));
        var service = new VertragsService(factory, uhr);
        await service.SynchronisiereAsync(salon.Id);           // 31.01. abends: noch Probezeit
        uhr.Jetzt = new DateTime(2027, 2, 1, 0, 30, 0, DateTimeKind.Utc);
        await service.SynchronisiereAsync(salon.Id);           // 01.02.: erste Rechnung

        await using var pruefung = await factory.CreateDbContextAsync();
        var abo = Assert.Single(pruefung.NutzungsAbos);
        Assert.Equal((new DateTime(2027, 2, 1), new DateTime(2027, 3, 1), 5000m, false),
            (abo.ZeitraumVonUtc, abo.ZeitraumBisUtc, abo.PreisSnapshot, abo.IstTeilperiode));
        Assert.Equal(new DateTime(2027, 2, 16), abo.KassenSperreAbUtc);
    }

    [Fact]
    public async Task Vom_Admin_Gesetztes_Probedatum_Gilt_Einschliesslich_Des_Tages()
    {
        // Admin wählt im Datumsfeld den 31.01.2027 → Probezeit bis einschließlich 31.01.
        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Name = "A", IstAktiv = true, IstFreigegeben = true };
        Guid adminId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            db.PlattformPreise.Add(new PlattformPreis { Intervall = AboIntervall.Monatlich, Preis = 5000, IstAktuell = true, GueltigAbUtc = new DateTime(2026, 1, 1) });
            adminId = LadenRechteTests.LegeKontoAn(db, "admin", ENote.Core.DataModels.Identity.AppRollen.SuperAdmin);
            await db.SaveChangesAsync();
        }

        var admin = new AdminService(factory, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<AdminService>.Instance, null!);
        await admin.SetzeProbezeitAsync(adminId, salon.Id, new DateTime(2027, 1, 31));

        var service = new VertragsService(factory, new Uhr(new DateTime(2027, 2, 1, 0, 30, 0, DateTimeKind.Utc)));
        await service.SynchronisiereAsync(salon.Id);

        await using var pruefung = await factory.CreateDbContextAsync();
        Assert.True((await pruefung.Salons.SingleAsync()).ProbeBisUtc > new DateTime(2027, 1, 31, 23, 0, 0));
        var abo = Assert.Single(pruefung.NutzungsAbos);
        Assert.Equal((new DateTime(2027, 2, 1), 5000m), (abo.ZeitraumVonUtc, abo.PreisSnapshot));
    }
}
