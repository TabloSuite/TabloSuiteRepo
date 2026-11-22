using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Fehlersuche 21.11.2026 (Test-Uhr, 13 Monate später): Die Abonnement-Seite zeigte nur die letzten 12 Perioden. Eine ältere,
/// nie gemeldete Rechnung verschwand – der Besitzer konnte sie nicht mehr melden, die Kasse blieb wegen ihr gesperrt.
/// </summary>
public class AboAlteOffeneRechnungTests
{
    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(jetzt, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Alte_Unbezahlte_Rechnung_Bleibt_Sichtbar()
    {
        var f = TestDbContextFactory.Create();
        var beginn = new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var salon = new Salon { Name = "Alt offen", IstAktiv = true, IstFreigegeben = true, ProbeBisUtc = beginn.AddTicks(-1), VertragsBeginnUtc = beginn };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Besitzer" };
        var perioden = Enumerable.Range(0, 14).Select(i => new NutzungsAbo
        {
            SalonId = salon.Id, Intervall = AboIntervall.Monatlich, ZeitraumVonUtc = beginn.AddMonths(i), ZeitraumBisUtc = beginn.AddMonths(i + 1),
            PreisSnapshot = 3600m, MonatsBasisSnapshot = 3600m, FaelligUtc = beginn.AddMonths(i),
            ZahlungStatus = i == 1 ? AboZahlungStatus.Offen : AboZahlungStatus.Bestaetigt   // März 2027 nie gemeldet
        }).ToList();
        await using (var db = await f.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.Besitzer };
            db.Roles.Add(rolle); db.Users.Add(besitzer); db.UserRoles.Add(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id });
            db.Salons.Add(salon); db.UserSalons.Add(new UserSalon { AppUserId = besitzer.Id, SalonId = salon.Id });
            db.NutzungsAbos.AddRange(perioden);
            await db.SaveChangesAsync();
        }
        var vertrag = await new VertragsService(f, new Uhr(beginn.AddMonths(13).AddDays(5))).GetLadenVertragAsync(besitzer.Id, salon.Id);
        Assert.Contains(vertrag.Perioden, p => p.Id == perioden[1].Id && p.Status == AboZahlungStatus.Offen);
        Assert.True(vertrag.Perioden.Count >= 13);
    }
}
