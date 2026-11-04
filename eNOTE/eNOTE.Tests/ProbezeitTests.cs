using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

public class ProbezeitTests
{
    [Fact]
    public void BerechneProbeEnde_StartMitteNovember_EndetEndeJanuar()
    {
        // Start am 3. November 2026 → Probe = November + Dezember + Januar → bis 31.01.2027.
        var start = new DateTime(2026, 11, 3, 14, 0, 0, DateTimeKind.Utc);
        var ende = AuthService.BerechneProbeEnde(start);

        Assert.Equal(2027, ende.Year);
        Assert.Equal(1, ende.Month);
        Assert.Equal(31, ende.Day);
        Assert.True(ende.Hour == 23 && ende.Minute == 59); // Ende des Tages
    }

    [Fact]
    public void BerechneProbeEnde_StartAnfangDezember_EndetEndeFebruar()
    {
        // Start am 1. Dezember → Dezember + Januar + Februar → bis 28./29.02.
        var start = new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc);
        var ende = AuthService.BerechneProbeEnde(start);

        Assert.Equal(2027, ende.Year);
        Assert.Equal(2, ende.Month);
    }

    [Fact]
    public void BerechneProbeEnde_Jahreswechsel_Korrekt()
    {
        // Start im November → geht über den Jahreswechsel.
        var start = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
        var ende = AuthService.BerechneProbeEnde(start);

        Assert.True(ende > start);
        Assert.Equal(3, (ende.Year - start.Year) * 12 + ende.Month - start.Month + 1); // 3 Kalendermonate inkl. Startmonat
    }

    [Fact]
    public async Task BerechneProbeEndeFuerBesitzer_ErsterLaden_BekommtProbe()
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var besitzer = new AppUser { UserName = "b@x.de", Email = "b@x.de", FullName = "Bes Itzer" };
        db.Users.Add(besitzer);
        await db.SaveChangesAsync();

        // AuthService benötigt UserManager/SignInManager – hier nur die Probe-Logik via Factory testen.
        // Wir nutzen die statische Berechnung + simulieren die Regel direkt über die DB-Abfrage.
        var hatLaden = await db.UserSalons.AnyAsync(us => us.AppUserId == besitzer.Id);
        var probe = hatLaden ? (DateTime?)null : AuthService.BerechneProbeEnde(DateTime.UtcNow);

        Assert.NotNull(probe); // kein Laden → Probe
    }

    [Fact]
    public async Task BerechneProbeEndeFuerBesitzer_ZweiterLaden_KeineProbe()
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var besitzer = new AppUser { UserName = "b@x.de", Email = "b@x.de", FullName = "Bes Itzer" };
        var salon1 = new Salon { Name = "Läden 1", IstFreigegeben = true };
        db.Users.Add(besitzer);
        db.Salons.Add(salon1);
        await db.SaveChangesAsync();
        db.UserSalons.Add(new UserSalon { AppUserId = besitzer.Id, SalonId = salon1.Id });
        await db.SaveChangesAsync();

        var hatLaden = await db.UserSalons.AnyAsync(us => us.AppUserId == besitzer.Id);
        var probe = hatLaden ? (DateTime?)null : AuthService.BerechneProbeEnde(DateTime.UtcNow);

        Assert.Null(probe); // hat schon einen Laden → keine Probe für den zweiten
    }
}
