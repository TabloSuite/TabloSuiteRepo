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

public class StornoTests
{
    [Fact]
    public void Teilerstattungen_Verteilen_Rabatt_Anteilig_Und_Lassen_Keinen_Rundungsrest()
    {
        // 3 Stück, Zeilensumme nach Rabatt 1.000 F
        var a = StornoRechner.Betrag(1000m, 3, 0, 0m, 1);
        var b = StornoRechner.Betrag(1000m, 3, 1, a, 1);
        var c = StornoRechner.Betrag(1000m, 3, 2, a + b, 1);

        Assert.Equal((333.33m, 333.33m, 333.34m), (a, b, c));
        Assert.Equal(1000m, a + b + c);
        Assert.Equal(1000m, StornoRechner.Betrag(1000m, 3, 0, 0m, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => StornoRechner.Betrag(1000m, 3, 2, 666.66m, 2));
        Assert.Equal(0m, StornoRechner.Rest(500m, 700m));
    }

    private sealed record Basis(IDbContextFactory<ApplicationDbContext> F, Salon Salon, AppUser Besitzer, AppUser Kassierer,
        Leistung Huile, KassenService Kasse, StornoService Service, Guid SitzungId);

    private static async Task<Basis> BasisAsync()
    {
        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Name = "S", IstAktiv = true, IstFreigegeben = true };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Ama" };
        var kassierer = new AppUser { Id = Guid.NewGuid(), FullName = "Awa" };
        var huile = new Leistung { SalonId = salon.Id, Name = "Huile", Preis = 3000, Kaufpreis = 1800, HatStueckzahl = true, Stueckzahl = 10, IstAktiv = true };
        await using (var db = await factory.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.Besitzer, NormalizedName = "BESITZER" };
            db.Roles.Add(rolle);
            db.Users.AddRange(besitzer, kassierer);
            db.UserRoles.Add(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id });
            db.Salons.Add(salon);
            db.UserSalons.AddRange(
                new UserSalon { AppUserId = besitzer.Id, SalonId = salon.Id, BesitzerPinHash = KassenPinService.HashPin("1234"), KassenPinHash = KassenPinService.HashPin("1000") },
                new UserSalon { AppUserId = kassierer.Id, SalonId = salon.Id, KassenPinHash = KassenPinService.HashPin("2222") });
            db.Leistungen.Add(huile);
            await db.SaveChangesAsync();
        }

        var umsatz = new FakeUmsatzBenachrichtigungService();
        var kasse = new KassenService(factory, umsatz);
        var sitzung = (await kasse.StarteSitzungAsync(salon.Id, kassierer.Id, 10000)).Sitzung!;
        return new Basis(factory, salon, besitzer, kassierer, huile, kasse, new StornoService(factory, kasse, umsatz), sitzung.Id);
    }

    private static async Task<Verkauf> VerkaufeAsync(Basis b, int menge, params KassenService.ZahlungInput[] zahlungen)
    {
        var ergebnis = await b.Kasse.BucheVerkaufAsync(b.SitzungId,
            [new KassenService.VerkaufsPositionInput(b.Huile.Id, menge, null, false)], zahlungen);
        Assert.True(ergebnis.Erfolg, ergebnis.Fehler);
        return ergebnis.Verkauf!;
    }

    [Fact]
    public async Task Bar_Erstattung_In_Teilen_Bucht_Gegen_Und_Gibt_Ware_Zurueck()
    {
        var b = await BasisAsync();
        var verkauf = await VerkaufeAsync(b, 3, new KassenService.ZahlungInput(ZahlungsArt.Bar, Gegeben: 10000));
        var posId = verkauf.Positionen.Single().Id;
        Assert.Equal(19000m, await b.Kasse.GetBarSollAsync(b.SitzungId));

        // Kassierer ohne/mit falscher Besitzer-PIN, ohne Grund → abgelehnt
        Assert.False((await b.Service.ErstatteAsync(b.Kassierer.Id, b.SitzungId, verkauf.Id, [new(posId, 1)], "0:", true, "Défaut", null)).Erfolg);
        Assert.False((await b.Service.ErstatteAsync(b.Kassierer.Id, b.SitzungId, verkauf.Id, [new(posId, 1)], "0:", true, "Défaut", "9999")).Erfolg);
        Assert.False((await b.Service.ErstatteAsync(b.Kassierer.Id, b.SitzungId, verkauf.Id, [new(posId, 1)], "0:", true, " ", "1234")).Erfolg);

        var erste = await b.Service.ErstatteAsync(b.Kassierer.Id, b.SitzungId, verkauf.Id, [new(posId, 1)], "0:", true, "Article défectueux", "1234");
        Assert.True(erste.Erfolg, erste.Fehler);
        Assert.Equal(3000m, erste.Betrag);
        Assert.Equal(16000m, await b.Kasse.GetBarSollAsync(b.SitzungId));

        var vorlage = (await b.Service.GetVorlageAsync(verkauf.Id))!;
        Assert.Equal((2, 3000m), (vorlage.Positionen.Single().Offen, vorlage.BereitsErstattet));

        // Mehr als offen geht nicht, Rest schon; danach ist alles erstattet
        Assert.False((await b.Service.ErstatteAsync(b.Kassierer.Id, b.SitzungId, verkauf.Id, [new(posId, 3)], "0:", true, "Erreur", "1234")).Erfolg);
        var zweite = await b.Service.ErstatteAsync(b.Kassierer.Id, b.SitzungId, verkauf.Id, [new(posId, 2)], "0:", false, "Erreur de caisse", "1234");
        Assert.True(zweite.Erfolg, zweite.Fehler);
        Assert.True((await b.Service.GetVorlageAsync(verkauf.Id))!.VollstaendigErstattet);
        Assert.False((await b.Service.ErstatteAsync(b.Kassierer.Id, b.SitzungId, verkauf.Id, [new(posId, 1)], "0:", true, "x", "1234")).Erfolg);

        // Eine Rückerstattung selbst kann nicht erstattet werden
        Assert.Null(await b.Service.GetVorlageAsync(erste.Storno!.Id));

        await using var db = await b.F.CreateDbContextAsync();
        // 10 − 3 verkauft + 1 zurück ins Lager (zweite Erstattung ohne Rückgabe)
        Assert.Equal(8m, (await db.Leistungen.SingleAsync(l => l.Id == b.Huile.Id)).Stueckzahl);
        Assert.Equal(10000m, await b.Kasse.GetBarSollAsync(b.SitzungId));

        var uebersicht = await new FinanzService(b.F).GetFinanzUebersichtAsync(b.Salon.Id, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
        Assert.Equal((0m, 1, 9000m), (uebersicht.EinnahmenGesamt, uebersicht.AnzahlVerkaeufe, uebersicht.Erstattungen));

        var sitzung = (await b.Kasse.GetSitzungsUebersichtAsync(b.SitzungId))!;
        Assert.Equal((1, 2, 9000m, 10000m), (sitzung.AnzahlVerkaeufe, sitzung.AnzahlErstattungen, sitzung.Erstattungen, sitzung.BarSoll));
    }

    [Fact]
    public async Task Erstattung_Nur_Ueber_Zahlwege_Des_Originals_Und_Bar_Nur_Wenn_Genug_In_Der_Kasse()
    {
        var b = await BasisAsync();
        var verkauf = await VerkaufeAsync(b, 2, new KassenService.ZahlungInput(ZahlungsArt.Karte));
        var posId = verkauf.Positionen.Single().Id;

        // Karte bezahlt → kein Bar-Weg
        var bar = await b.Service.ErstatteAsync(b.Besitzer.Id, b.SitzungId, verkauf.Id, [new(posId, 1)], "0:", true, "Erreur", "1234");
        Assert.False(bar.Erfolg);
        var karte = await b.Service.ErstatteAsync(b.Besitzer.Id, b.SitzungId, verkauf.Id, [new(posId, 2)], "1:", true, "Erreur", "1234");
        Assert.True(karte.Erfolg, karte.Fehler);
        Assert.Equal(10000m, await b.Kasse.GetBarSollAsync(b.SitzungId)); // Bargeld unberührt

        // Bar-Verkauf über 12.000 bei 10.000 Startbestand; Besitzer nimmt 15.000 heraus → nur 7.000 in der Kasse
        var gross = await VerkaufeAsync(b, 4, new KassenService.ZahlungInput(ZahlungsArt.Bar, Gegeben: 12000));
        var bestand = new BestandsService(b.F, b.Kasse);
        Assert.True((await bestand.EntnehmeBargeldAsync(b.Besitzer.Id, b.SitzungId, "1234", 15000, null)).Erfolg);
        var zuViel = await b.Service.ErstatteAsync(b.Besitzer.Id, b.SitzungId, gross.Id, [new(gross.Positionen.Single().Id, 4)], "0:", true, "Erreur", "1234");
        Assert.False(zuViel.Erfolg);
        Assert.Contains("espèces", zuViel.Fehler);
    }
}
