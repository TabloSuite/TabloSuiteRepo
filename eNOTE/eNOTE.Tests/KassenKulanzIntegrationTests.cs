using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Salons;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Katalog;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

public class KassenKulanzIntegrationTests
{
    [Fact]
    public async Task Sitzung_Starten_Verkaufen_Und_Abschliessen()
    {
        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Name = "A", IstFreigegeben = true };
        var user = new AppUser();
        var leistung = new Leistung { Name = "Coupe", Preis = 1000, IstAktiv = true };
        await using (var db = await factory.CreateDbContextAsync())
        {
            leistung.Salon = salon;
            db.Leistungen.Add(leistung);
            db.UserSalons.Add(new UserSalon { AppUser = user, Salon = salon, KassenPinHash = KassenPinService.HashPin("1234") });
            await db.SaveChangesAsync();
        }

        var kasse = new KassenService(factory, new FakeUmsatzBenachrichtigungService());
        var start = await kasse.StarteSitzungAsync(salon.Id, user.Id, 0);
        Assert.True(start.Erfolg);

        // Zweite Sitzung am selben Salon ist nicht erlaubt.
        Assert.False((await kasse.StarteSitzungAsync(salon.Id, user.Id, 0)).Erfolg);

        var verkauf = await kasse.BucheVerkaufAsync(start.Sitzung!.Id,
            [new KassenService.VerkaufsPositionInput(leistung.Id, 1, null, false)], ZahlungsArt.Bar, 1000, false);
        Assert.True(verkauf.Erfolg);

        var abschluss = await kasse.SchliesseSitzungAsync(start.Sitzung.Id, 1000, "Fin de journée");
        Assert.True(abschluss.Erfolg);

        // Nach dem Abschluss ist keine offene Sitzung mehr vorhanden.
        await using var kontrolle = await factory.CreateDbContextAsync();
        Assert.Null(await kontrolle.KassenSitzungen
            .FirstOrDefaultAsync(k => k.SalonId == salon.Id && k.Status == KassenSitzungStatus.Offen));
        Assert.Single(await kontrolle.Verkaeufe.ToListAsync());
    }
}
