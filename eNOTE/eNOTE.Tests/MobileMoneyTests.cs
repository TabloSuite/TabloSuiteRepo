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

public class MobileMoneyTests
{
    private sealed record Basis(
        IDbContextFactory<ApplicationDbContext> Factory, Salon Salon, AppUser Besitzer, Leistung Leistung,
        MobileMoneyAnbieter Flooz, MobileMoneyAnbieter Mixx, MobileMoneyAnbieter Wave, KassenService Kasse, Guid SitzungId);

    private static async Task<Basis> BasisAsync()
    {
        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Name = "Salon", Land = "Togo", IstAktiv = true, IstFreigegeben = true };
        var besitzer = new AppUser { FullName = "Ama", UserName = "ama" };
        var rolle = new Role { Name = AppRollen.Besitzer, NormalizedName = "BESITZER" };
        var leistung = new Leistung { SalonId = salon.Id, Name = "Tresses", Preis = 15000, IstAktiv = true };
        var flooz = new MobileMoneyAnbieter { Land = "Togo", Name = "Flooz", Reihenfolge = 1 };
        var mixx = new MobileMoneyAnbieter { Land = "Togo", Name = "Mixx by Yas", Reihenfolge = 2 };
        var wave = new MobileMoneyAnbieter { Land = "Sénégal", Name = "Wave" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            db.Users.Add(besitzer);
            db.Roles.Add(rolle);
            db.UserRoles.Add(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id });
            db.UserSalons.Add(new UserSalon { AppUserId = besitzer.Id, SalonId = salon.Id, KassenPinHash = KassenPinService.HashPin("1234") });
            db.Leistungen.Add(leistung);
            db.MobileMoneyAnbieter.AddRange(flooz, mixx, wave);
            await db.SaveChangesAsync();
        }

        var kasse = new KassenService(factory, new FakeUmsatzBenachrichtigungService());
        var sitzung = (await kasse.StarteSitzungAsync(salon.Id, besitzer.Id, 10000)).Sitzung!;
        return new Basis(factory, salon, besitzer, leistung, flooz, mixx, wave, kasse, sitzung.Id);
    }

    private static List<KassenService.VerkaufsPositionInput> EinePosition(Basis b) => [new(b.Leistung.Id, 1, null, false)];

    [Fact]
    public async Task Geteilte_Zahlung_Bar_Und_Mobile_Money_Speichert_Anteile_Und_Rueckgeld()
    {
        var b = await BasisAsync();

        var (erfolg, fehler, verkauf, _) = await b.Kasse.BucheVerkaufAsync(b.SitzungId, EinePosition(b),
        [
            new KassenService.ZahlungInput(ZahlungsArt.Bar, Betrag: 5000, Gegeben: 10000),
            new KassenService.ZahlungInput(ZahlungsArt.MobileMoney, AnbieterId: b.Flooz.Id, Referenz: "  TX-123  ")
        ]);

        Assert.True(erfolg, fehler);
        Assert.Equal(ZahlungsArt.Gemischt, verkauf!.ZahlungsArt);
        Assert.Equal(5000, verkauf.Rueckgeld);
        await using var db = await b.Factory.CreateDbContextAsync();
        var zahlungen = await db.VerkaufZahlungen.Where(z => z.VerkaufId == verkauf.Id).OrderBy(z => z.Reihenfolge).ToListAsync();
        Assert.Equal([5000m, 10000m], zahlungen.Select(z => z.Betrag));
        Assert.Equal("Flooz", zahlungen[1].AnbieterName);
        Assert.Equal("TX-123", zahlungen[1].Referenz);

        // Kassen-Soll: nur der Baranteil zählt.
        Assert.Equal(15000m, await b.Kasse.GetBarSollAsync(b.SitzungId));
    }

    [Fact]
    public async Task Nur_Mobile_Money_Ergibt_Zahlart_MobileMoney()
    {
        var b = await BasisAsync();
        var (erfolg, fehler, verkauf, _) = await b.Kasse.BucheVerkaufAsync(b.SitzungId, EinePosition(b),
            [new KassenService.ZahlungInput(ZahlungsArt.MobileMoney, AnbieterId: b.Mixx.Id)]);

        Assert.True(erfolg, fehler);
        Assert.Equal(ZahlungsArt.MobileMoney, verkauf!.ZahlungsArt);
        Assert.Null(verkauf.Rueckgeld);
        Assert.Equal(10000m, await b.Kasse.GetBarSollAsync(b.SitzungId));
    }

    [Fact]
    public async Task Anbieter_Eines_Anderen_Landes_Wird_Abgelehnt()
    {
        var b = await BasisAsync();
        var (erfolg, fehler, _, _) = await b.Kasse.BucheVerkaufAsync(b.SitzungId, EinePosition(b),
            [new KassenService.ZahlungInput(ZahlungsArt.MobileMoney, AnbieterId: b.Wave.Id)]);

        Assert.False(erfolg);
        Assert.Contains("pas accepté", fehler);
    }

    [Theory]
    [InlineData(4000, "Il manque")]
    [InlineData(16000, "dépassent")]
    public async Task Summe_Der_Anteile_Muss_Total_Treffen(int karte, string erwartet)
    {
        var b = await BasisAsync();
        var (erfolg, fehler, _, _) = await b.Kasse.BucheVerkaufAsync(b.SitzungId, EinePosition(b),
        [
            new KassenService.ZahlungInput(ZahlungsArt.Karte, Betrag: karte),
            new KassenService.ZahlungInput(ZahlungsArt.MobileMoney, Betrag: 10000, AnbieterId: b.Flooz.Id)
        ]);

        Assert.False(erfolg);
        Assert.Contains(erwartet, fehler);
    }

    [Fact]
    public async Task Mobile_Money_Ohne_Anbieter_Und_Bar_Ohne_Betrag_Werden_Abgelehnt()
    {
        var b = await BasisAsync();
        var ohneAnbieter = await b.Kasse.BucheVerkaufAsync(b.SitzungId, EinePosition(b),
            [new KassenService.ZahlungInput(ZahlungsArt.MobileMoney)]);
        var zuWenigBar = await b.Kasse.BucheVerkaufAsync(b.SitzungId, EinePosition(b),
            [new KassenService.ZahlungInput(ZahlungsArt.Bar, Gegeben: 10000)]);

        Assert.Contains("opérateur", ohneAnbieter.Fehler);
        Assert.Contains("inférieur", zuWenigBar.Fehler);
    }

    [Fact]
    public async Task Laden_Akzeptiert_Standardmaessig_Alle_Anbieter_Des_Landes_Und_Explizite_Auswahl()
    {
        var b = await BasisAsync();
        var service = new MobileMoneyService(b.Factory);

        Assert.Equal(["Flooz", "Mixx by Yas"], (await service.GetErlaubteAnbieterAsync(b.Salon.Id)).Select(a => a.Name));

        Assert.True((await service.SetzeAuswahlAsync(b.Besitzer.Id, b.Salon.Id, [b.Mixx.Id])).Erfolg);
        Assert.Equal(["Mixx by Yas"], (await service.GetErlaubteAnbieterAsync(b.Salon.Id)).Select(a => a.Name));
        var flooz = await b.Kasse.BucheVerkaufAsync(b.SitzungId, EinePosition(b),
            [new KassenService.ZahlungInput(ZahlungsArt.MobileMoney, AnbieterId: b.Flooz.Id)]);
        Assert.False(flooz.Erfolg);

        // Leere Auswahl = kein Mobile Money (nicht wieder „alle des Landes“).
        Assert.True((await service.SetzeAuswahlAsync(b.Besitzer.Id, b.Salon.Id, [])).Erfolg);
        Assert.Empty(await service.GetErlaubteAnbieterAsync(b.Salon.Id));
    }

    [Fact]
    public async Task Nur_Besitzer_Darf_Auswahl_Aendern()
    {
        var b = await BasisAsync();
        var service = new MobileMoneyService(b.Factory);

        var (erfolg, _) = await service.SetzeAuswahlAsync(Guid.NewGuid(), b.Salon.Id, [b.Flooz.Id]);

        Assert.False(erfolg);
        Assert.Equal(2, (await service.GetErlaubteAnbieterAsync(b.Salon.Id)).Count);
    }

    [Fact]
    public async Task Finanzuebersicht_Teilt_Gemischte_Zahlungen_Nach_Art_Und_Anbieter_Auf()
    {
        var b = await BasisAsync();
        await b.Kasse.BucheVerkaufAsync(b.SitzungId, EinePosition(b),
        [
            new KassenService.ZahlungInput(ZahlungsArt.Bar, Betrag: 5000, Gegeben: 5000),
            new KassenService.ZahlungInput(ZahlungsArt.MobileMoney, AnbieterId: b.Flooz.Id)
        ]);
        await b.Kasse.BucheVerkaufAsync(b.SitzungId, EinePosition(b),
            [new KassenService.ZahlungInput(ZahlungsArt.Karte)]);

        var finanz = new FinanzService(b.Factory);
        var uebersicht = await finanz.GetFinanzUebersichtAsync(b.Salon.Id, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        var tag = Assert.Single(await finanz.GetTagesUmsaetzeAsync(b.Salon.Id));
        var letzte = await finanz.GetLetzteVerkaeufeAsync(b.Salon.Id);

        Assert.Equal((5000m, 15000m, 10000m, 30000m, 2), (uebersicht.EinnahmenBar, uebersicht.EinnahmenKarte, uebersicht.EinnahmenMobileMoney, uebersicht.EinnahmenGesamt, uebersicht.AnzahlVerkaeufe));
        Assert.Equal(("Flooz", 10000m), (uebersicht.MobileMoneyNachAnbieter.Single().Name, uebersicht.MobileMoneyNachAnbieter.Single().Betrag));
        Assert.Equal((5000m, 15000m, 10000m, 30000m), (tag.Bar, tag.Karte, tag.MobileMoney, tag.Gesamt));
        Assert.Contains(letzte, v => v.ZahlungText == "Espèces + Flooz");
    }

    [Fact]
    public void KassenRechner_Altbestand_Ohne_Anteile_Gilt_Als_Ein_Anteil()
    {
        var alt = new Verkauf { ZahlungsArt = ZahlungsArt.Karte, GesamtSumme = 2500 };
        var anteil = Assert.Single(KassenRechner.Anteile(alt));
        Assert.Equal((ZahlungsArt.Karte, 2500m), (anteil.Art, anteil.Betrag));
        Assert.Equal("Un seul paiement en espèces par vente.",
            KassenRechner.PruefeZahlungen(100, [(ZahlungsArt.Bar, 50m), (ZahlungsArt.Bar, 50m)]));
    }

    [Fact]
    public async Task Laden_ohne_Land_aus_der_Selbstregistrierung_akzeptiert_die_Anbieter_aus_Togo()
    {
        // Simulation S1: Selbst registrierte Läden haben kein Land – sie hatten keinen einzigen Mobile-Money-Anbieter.
        var b = await BasisAsync();
        await using (var db = await b.Factory.CreateDbContextAsync())
        {
            var salon = await db.Salons.SingleAsync(s => s.Id == b.Salon.Id);
            salon.Land = null;
            await db.SaveChangesAsync();
        }
        var service = new MobileMoneyService(b.Factory);

        Assert.Equal(["Flooz", "Mixx by Yas"], (await service.GetErlaubteAnbieterAsync(b.Salon.Id)).Select(a => a.Name));
        Assert.Equal(["Flooz", "Mixx by Yas"], (await service.GetAuswahlFuerLadenAsync(b.Salon.Id)).Select(a => a.Anbieter.Name));
        var verkauf = await b.Kasse.BucheVerkaufAsync(b.SitzungId, EinePosition(b),
            [new KassenService.ZahlungInput(ZahlungsArt.MobileMoney, AnbieterId: b.Flooz.Id)]);
        Assert.True(verkauf.Erfolg, verkauf.Fehler);
        var wave = await b.Kasse.BucheVerkaufAsync(b.SitzungId, EinePosition(b),
            [new KassenService.ZahlungInput(ZahlungsArt.MobileMoney, AnbieterId: b.Wave.Id)]);
        Assert.False(wave.Erfolg);
    }
}
