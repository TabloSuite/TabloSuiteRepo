using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Salons;
using ENote.Core.DataModels.Identity;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

public class KassenKulanzIntegrationTests
{
    private class Uhr : TimeProvider
    {
        public DateTime Jetzt = new(2026, 11, 15);
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Alte_Sitzung_Darf_Nicht_Verkaufen_Aber_Abschliessen()
    {
        var factory = TestDbContextFactory.Create();
        var uhr = new Uhr();
        var salon = new Salon { Name = "A", IstFreigegeben = true, VertragsBeginnUtc = new DateTime(2026, 11, 1), IndividuellerMonatsPreis = 1000 };
        var user = new AppUser();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UserSalons.Add(new UserSalon { AppUser = user, Salon = salon, KassenPinHash = KassenPinService.HashPin("1234") });
            await db.SaveChangesAsync();
        }
        var kasse = new KassenService(factory, new FakeUmsatzBenachrichtigungService(), uhr);
        var start = await kasse.StarteSitzungAsync(salon.Id, user.Id, 0);
        Assert.True(start.Erfolg);
        uhr.Jetzt = new DateTime(2026, 11, 16);
        var verkauf = await kasse.BucheVerkaufAsync(start.Sitzung!.Id,
            [new KassenService.VerkaufsPositionInput(Guid.NewGuid(), 1, null, false)], ZahlungsArt.Bar, 1000, false);
        Assert.False(verkauf.Erfolg);
        Assert.Contains("suspendue", verkauf.Fehler);
        Assert.False((await kasse.StarteSitzungAsync(salon.Id, user.Id, 0)).Erfolg);
        Assert.True((await kasse.SchliesseSitzungAsync(start.Sitzung.Id, 0, "Fin de journée")).Erfolg);
        await using var kontrolle = await factory.CreateDbContextAsync();
        Assert.Empty(await kontrolle.Verkaeufe.ToListAsync());
    }
}
