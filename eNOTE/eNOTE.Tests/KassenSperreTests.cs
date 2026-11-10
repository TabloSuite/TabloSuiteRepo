using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

/// <summary>Abo unbezahlt nach Frist: Kasse serverseitig gesperrt, Abschluss bleibt möglich.</summary>
public class KassenSperreTests
{
    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public DateTime Jetzt { get; set; } = jetzt;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Unbezahltes_Abo_Nach_Frist_Sperrt_Start_Und_Verkauf_Aber_Nicht_Den_Abschluss()
    {
        var factory = TestDbContextFactory.Create();
        var faellig = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var uhr = new Uhr(faellig.AddDays(2));
        var salon = new Salon
        {
            Name = "Salon", IstAktiv = true, IstFreigegeben = true, IndividuellerMonatsPreis = 3500,
            VertragsBeginnUtc = faellig, VertragsIntervall = AboIntervall.Monatlich
        };
        var kassierer = new AppUser { Id = Guid.NewGuid(), FullName = "Awa" };
        var leistung = new Leistung { SalonId = salon.Id, Name = "Coupe", Preis = 2000, IstAktiv = true };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            db.Users.Add(kassierer);
            db.UserSalons.Add(new UserSalon { AppUserId = kassierer.Id, SalonId = salon.Id, KassenPinHash = KassenPinService.HashPin("2222") });
            db.Leistungen.Add(leistung);
            await db.SaveChangesAsync();
        }

        var kasse = new KassenService(factory, new FakeUmsatzBenachrichtigungService(), uhr);
        List<KassenService.VerkaufsPositionInput> pos = [new(leistung.Id, 1, null, false)];

        // Innerhalb der Kulanz: Start und Verkauf möglich (Periode wird dabei automatisch erzeugt).
        var sitzung = (await kasse.StarteSitzungAsync(salon.Id, kassierer.Id, 0)).Sitzung!;
        Assert.True((await kasse.BucheVerkaufAsync(sitzung.Id, pos, [new KassenService.ZahlungInput(ZahlungsArt.Bar, Gegeben: 2000)])).Erfolg);

        // Ab dem 15. Tag: keine Verkäufe mehr …
        uhr.Jetzt = faellig.AddDays(15);
        var verkauf = await kasse.BucheVerkaufAsync(sitzung.Id, pos, [new KassenService.ZahlungInput(ZahlungsArt.Bar, Gegeben: 2000)]);
        Assert.False(verkauf.Erfolg);
        Assert.Contains("suspendue", verkauf.Fehler);

        // … aber der Abschluss der laufenden Sitzung bleibt möglich.
        Assert.True((await kasse.SchliesseSitzungAsync(sitzung.Id, 2000, null)).Erfolg);

        // Neue Sitzung wird verweigert.
        var neu = await kasse.StarteSitzungAsync(salon.Id, kassierer.Id, 0);
        Assert.False(neu.Erfolg);
        Assert.Contains("suspendue", neu.Fehler);

        // Admin bestätigt den Zahlungseingang → wieder frei.
        await using (var db = await factory.CreateDbContextAsync())
        {
            var abo = await db.NutzungsAbos.SingleAsync();
            abo.ZahlungStatus = AboZahlungStatus.Bestaetigt;
            await db.SaveChangesAsync();
        }
        Assert.True((await kasse.StarteSitzungAsync(salon.Id, kassierer.Id, 0)).Erfolg);
    }

    [Fact]
    public async Task Ohne_Konfigurierten_Tarif_Wird_Die_Kasse_Nicht_Blockiert()
    {
        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Name = "Salon", IstAktiv = true, IstFreigegeben = true };
        var kassierer = new AppUser { Id = Guid.NewGuid(), FullName = "Awa" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            db.Users.Add(kassierer);
            db.UserSalons.Add(new UserSalon { AppUserId = kassierer.Id, SalonId = salon.Id, KassenPinHash = KassenPinService.HashPin("2222") });
            await db.SaveChangesAsync();
        }

        var kasse = new KassenService(factory, new FakeUmsatzBenachrichtigungService(), new Uhr(DateTime.UtcNow.AddMonths(2)));
        Assert.True((await kasse.StarteSitzungAsync(salon.Id, kassierer.Id, 0)).Erfolg);
    }
}
