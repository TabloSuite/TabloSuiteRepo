using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

public class KassenServiceTests
{
    private static async Task<(ApplicationDbContext Db, Salon Salon, AppUser Kassierer, UserSalon Zuordnung)> BasisAsync(
        IDbContextFactory<ApplicationDbContext> factory, string pin = "1234")
    {
        var db = await factory.CreateDbContextAsync();
        var salon = new Salon { Name = "Salon A", IstFreigegeben = true, IstAktiv = true };
        var kassierer = new AppUser { UserName = "k@x.de", Email = "k@x.de", FullName = "Kas Sierer" };
        db.Salons.Add(salon);
        db.Users.Add(kassierer);
        await db.SaveChangesAsync();

        var zuordnung = new UserSalon
        {
            AppUserId = kassierer.Id,
            SalonId = salon.Id,
            KassenPinHash = KassenPinService.HashPin(pin)
        };
        db.UserSalons.Add(zuordnung);
        await db.SaveChangesAsync();
        return (db, salon, kassierer, zuordnung);
    }

    [Fact]
    public async Task StarteSitzung_Legt_Offene_Sitzung_An()
    {
        var factory = TestDbContextFactory.Create();
        var (_, salon, kassierer, _) = await BasisAsync(factory);
        var service = new KassenService(factory, new FakeUmsatzBenachrichtigungService());

        var ergebnis = await service.StarteSitzungAsync(salon.Id, kassierer.Id, 5000);

        Assert.True(ergebnis.Erfolg);
        Assert.NotNull(ergebnis.Sitzung);
        Assert.Equal(5000, ergebnis.Sitzung!.Startbestand);
        Assert.Equal(KassenSitzungStatus.Offen, ergebnis.Sitzung.Status);
    }

    [Fact]
    public async Task StarteSitzung_Verhindert_Zweite_Offene_Sitzung()
    {
        var factory = TestDbContextFactory.Create();
        var (_, salon, kassierer, _) = await BasisAsync(factory);
        var service = new KassenService(factory, new FakeUmsatzBenachrichtigungService());

        await service.StarteSitzungAsync(salon.Id, kassierer.Id, 5000);
        var zweite = await service.StarteSitzungAsync(salon.Id, kassierer.Id, 3000);

        Assert.False(zweite.Erfolg);
    }

    [Fact]
    public async Task FindeZuordnungPerPin_Loest_Richtigen_Laden_Auf()
    {
        var factory = TestDbContextFactory.Create();
        var db = await factory.CreateDbContextAsync();

        // Ein Kassierer, zwei Läden, unterschiedliche PINs.
        var salonA = new Salon { Name = "Salon A", IstFreigegeben = true };
        var salonB = new Salon { Name = "Salon B", IstFreigegeben = true };
        var kassierer = new AppUser { UserName = "k@x.de", Email = "k@x.de", FullName = "Kas" };
        db.Salons.AddRange(salonA, salonB);
        db.Users.Add(kassierer);
        await db.SaveChangesAsync();

        db.UserSalons.AddRange(
            new UserSalon { AppUserId = kassierer.Id, SalonId = salonA.Id, KassenPinHash = KassenPinService.HashPin("1111") },
            new UserSalon { AppUserId = kassierer.Id, SalonId = salonB.Id, KassenPinHash = KassenPinService.HashPin("2222") });
        await db.SaveChangesAsync();

        var service = new KassenService(factory, new FakeUmsatzBenachrichtigungService());
        var (erfolg, _, zuordnung, _) = await service.FindeZuordnungPerPinAsync(kassierer.Id, "2222");

        Assert.True(erfolg);
        Assert.Equal(salonB.Id, zuordnung!.SalonId); // PIN 2222 gehört zu Laden B
    }

    [Fact]
    public async Task FindeZuordnungPerPin_Falsche_Pin_Schlaegt_Fehl_Und_Sperrt_Nach_Max()
    {
        var factory = TestDbContextFactory.Create();
        var (_, _, kassierer, _) = await BasisAsync(factory, pin: "9999");
        var service = new KassenService(factory, new FakeUmsatzBenachrichtigungService());

        for (var i = 0; i < KassenPinService.MaxFehlversuche; i++)
        {
            var r = await service.FindeZuordnungPerPinAsync(kassierer.Id, "0000");
            Assert.False(r.Erfolg);
        }

        // Nach Max Fehlversuchen ist die PIN gesperrt – auch die richtige PIN schlägt fehl.
        var gesperrt = await service.FindeZuordnungPerPinAsync(kassierer.Id, "9999");
        Assert.False(gesperrt.Erfolg);
    }

    [Fact]
    public async Task BucheVerkauf_Bar_Berechnet_Rueckgeld_Und_Mindert_Bestand()
    {
        var factory = TestDbContextFactory.Create();
        var (db, salon, kassierer, _) = await BasisAsync(factory);
        var service = new KassenService(factory, new FakeUmsatzBenachrichtigungService());

        var artikel = new Leistung { SalonId = salon.Id, Name = "Boisson", Preis = 500, HatStueckzahl = true, Stueckzahl = 10, IstAktiv = true };
        db.Leistungen.Add(artikel);
        await db.SaveChangesAsync();

        var sitzung = (await service.StarteSitzungAsync(salon.Id, kassierer.Id, 5000)).Sitzung!;

        var positionen = new List<KassenService.VerkaufsPositionInput> { new(artikel.Id, 2, null, false) };
        var (erfolg, fehler, verkauf, _) = await service.BucheVerkaufAsync(sitzung.Id, positionen, ZahlungsArt.Bar, gegeben: 5000, karteBestaetigt: false);

        Assert.True(erfolg, fehler);
        Assert.Equal(1000, verkauf!.GesamtSumme);
        Assert.Equal(4000, verkauf.Rueckgeld);

        await using var pruefDb = await factory.CreateDbContextAsync();
        var neuGeladen = await pruefDb.Leistungen.AsNoTracking().FirstAsync(l => l.Id == artikel.Id);
        Assert.Equal(8, neuGeladen.Stueckzahl);
    }

    [Fact]
    public async Task BucheVerkauf_Bar_Lehnt_Unterdeckung_Ab()
    {
        var factory = TestDbContextFactory.Create();
        var (db, salon, kassierer, _) = await BasisAsync(factory);
        var service = new KassenService(factory, new FakeUmsatzBenachrichtigungService());

        var dienst = new Leistung { SalonId = salon.Id, Name = "Coupe", Preis = 3000, IstAktiv = true };
        db.Leistungen.Add(dienst);
        await db.SaveChangesAsync();

        var sitzung = (await service.StarteSitzungAsync(salon.Id, kassierer.Id, 0)).Sitzung!;
        var positionen = new List<KassenService.VerkaufsPositionInput> { new(dienst.Id, 1, null, false) };
        var (erfolg, fehler, _, _) = await service.BucheVerkaufAsync(sitzung.Id, positionen, ZahlungsArt.Bar, gegeben: 2000, karteBestaetigt: false);

        Assert.False(erfolg);
        Assert.Contains("inférieur", fehler);
    }

    [Fact]
    public async Task BucheVerkauf_Karte_Erfordert_Bestaetigung()
    {
        var factory = TestDbContextFactory.Create();
        var (db, salon, kassierer, _) = await BasisAsync(factory);
        var service = new KassenService(factory, new FakeUmsatzBenachrichtigungService());

        var dienst = new Leistung { SalonId = salon.Id, Name = "Coupe", Preis = 3000, IstAktiv = true };
        db.Leistungen.Add(dienst);
        await db.SaveChangesAsync();

        var sitzung = (await service.StarteSitzungAsync(salon.Id, kassierer.Id, 0)).Sitzung!;
        var positionen = new List<KassenService.VerkaufsPositionInput> { new(dienst.Id, 1, null, false) };

        var ohne = await service.BucheVerkaufAsync(sitzung.Id, positionen, ZahlungsArt.Karte, null, karteBestaetigt: false);
        Assert.False(ohne.Erfolg);

        var mit = await service.BucheVerkaufAsync(sitzung.Id, positionen, ZahlungsArt.Karte, null, karteBestaetigt: true);
        Assert.True(mit.Erfolg);
        Assert.Null(mit.Verkauf!.Rueckgeld); // Karte erzeugt kein Rückgeld
    }

    [Fact]
    public async Task BucheVerkauf_Erlaubt_Verkauf_Bei_Leerem_Bestand_Mit_Warnung()
    {
        var factory = TestDbContextFactory.Create();
        var (db, salon, kassierer, _) = await BasisAsync(factory);
        var service = new KassenService(factory, new FakeUmsatzBenachrichtigungService());

        var artikel = new Leistung { SalonId = salon.Id, Name = "Boisson", Preis = 500, HatStueckzahl = true, Stueckzahl = 0, IstAktiv = true };
        db.Leistungen.Add(artikel);
        await db.SaveChangesAsync();

        var sitzung = (await service.StarteSitzungAsync(salon.Id, kassierer.Id, 0)).Sitzung!;
        var positionen = new List<KassenService.VerkaufsPositionInput> { new(artikel.Id, 1, null, false) };
        var (erfolg, _, _, warnungen) = await service.BucheVerkaufAsync(sitzung.Id, positionen, ZahlungsArt.Bar, 500, false);

        Assert.True(erfolg);
        Assert.NotEmpty(warnungen); // „stock épuisé“
        await using var pruefDb = await factory.CreateDbContextAsync();
        Assert.Equal(-1, (await pruefDb.Leistungen.AsNoTracking().FirstAsync(l => l.Id == artikel.Id)).Stueckzahl);
    }

    [Fact]
    public async Task SchliesseSitzung_Berechnet_Soll_Ist_Differenz()
    {
        var factory = TestDbContextFactory.Create();
        var (db, salon, kassierer, _) = await BasisAsync(factory);
        var service = new KassenService(factory, new FakeUmsatzBenachrichtigungService());

        var dienst = new Leistung { SalonId = salon.Id, Name = "Coupe", Preis = 3000, IstAktiv = true };
        db.Leistungen.Add(dienst);
        await db.SaveChangesAsync();

        var sitzung = (await service.StarteSitzungAsync(salon.Id, kassierer.Id, 5000)).Sitzung!;
        var positionen = new List<KassenService.VerkaufsPositionInput> { new(dienst.Id, 1, null, false) };
        await service.BucheVerkaufAsync(sitzung.Id, positionen, ZahlungsArt.Bar, 3000, false); // Bar +3000
        await service.BucheVerkaufAsync(sitzung.Id, positionen, ZahlungsArt.Karte, null, true); // Karte zählt nicht

        // Soll = 5000 Start + 3000 Bar = 8000. Gezählt 8200 → Differenz +200.
        var (erfolg, _, abgeschlossen) = await service.SchliesseSitzungAsync(sitzung.Id, 8200, null);

        Assert.True(erfolg);
        Assert.Equal(8000, abgeschlossen!.BareinnahmenSoll);
        Assert.Equal(8200, abgeschlossen.EndbestandGezaehlt);
        Assert.Equal(200, abgeschlossen.Differenz);
        Assert.Equal(KassenSitzungStatus.Abgeschlossen, abgeschlossen.Status);
    }

    [Fact]
    public async Task BucheVerkauf_Sendet_Live_Benachrichtigung_Mit_Tagesumsatz()
    {
        var factory = TestDbContextFactory.Create();
        var (db, salon, kassierer, _) = await BasisAsync(factory);
        var benachrichtigung = new FakeUmsatzBenachrichtigungService();
        var service = new KassenService(factory, benachrichtigung);

        var dienst = new Leistung { SalonId = salon.Id, Name = "Coupe", Preis = 3000, IstAktiv = true };
        db.Leistungen.Add(dienst);
        await db.SaveChangesAsync();

        var sitzung = (await service.StarteSitzungAsync(salon.Id, kassierer.Id, 0)).Sitzung!;
        var positionen = new List<KassenService.VerkaufsPositionInput> { new(dienst.Id, 2, null, false) };
        var (erfolg, fehler, _, _) = await service.BucheVerkaufAsync(sitzung.Id, positionen, ZahlungsArt.Bar, 10000, false);

        Assert.True(erfolg, fehler);
        Assert.Equal(1, benachrichtigung.Aufrufe);
        Assert.Equal(salon.Id, benachrichtigung.LetzterSalonId);
        Assert.Equal(6000, benachrichtigung.LetzterTagesUmsatz); // 2 × 3000
    }
}
