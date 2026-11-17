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

/// <summary>
/// Nutzerwunsch 17.11.2026 « Qui encaisse ? »: Mehrere Personen kassieren gleichzeitig in derselben offenen Kasse.
/// Jeder Bon gehört der Person, die an ihrem Gerät per PIN angemeldet ist – nachvollziehbar bei einem Écart.
/// </summary>
public class QuiEncaisseTests
{
    private sealed record Basis(IDbContextFactory<ApplicationDbContext> F, Guid Salon, Guid Besitzer, Guid Awa, Guid Kofi, Guid Fremd,
        Leistung Artikel, KassenService Kasse, StornoService Storno, Guid Sitzung);

    private static async Task<Basis> BasisAsync()
    {
        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Name = "Boutique", IstAktiv = true, IstFreigegeben = true };
        var anderer = new Salon { Name = "Autre", IstAktiv = true, IstFreigegeben = true };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Ama Propriétaire" };
        var awa = new AppUser { Id = Guid.NewGuid(), FullName = "Awa" };
        var kofi = new AppUser { Id = Guid.NewGuid(), FullName = "Kofi" };
        var fremd = new AppUser { Id = Guid.NewGuid(), FullName = "Fremd" };
        var artikel = new Leistung { SalonId = salon.Id, Name = "Savon", Preis = 1000, IstAktiv = true };
        await using (var db = await factory.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.Besitzer, NormalizedName = "BESITZER" };
            db.Roles.Add(rolle);
            db.Users.AddRange(besitzer, awa, kofi, fremd);
            db.UserRoles.Add(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id });
            db.Salons.AddRange(salon, anderer);
            db.UserSalons.AddRange(
                new UserSalon { AppUserId = besitzer.Id, SalonId = salon.Id, BesitzerPinHash = KassenPinService.HashPin("1234") },
                new UserSalon { AppUserId = awa.Id, SalonId = salon.Id, KassenPinHash = KassenPinService.HashPin("2222") },
                new UserSalon { AppUserId = kofi.Id, SalonId = salon.Id, KassenPinHash = KassenPinService.HashPin("3333"), KannErstatten = true },
                new UserSalon { AppUserId = fremd.Id, SalonId = anderer.Id, KassenPinHash = KassenPinService.HashPin("4444") });
            db.Leistungen.Add(artikel);
            await db.SaveChangesAsync();
        }
        var umsatz = new FakeUmsatzBenachrichtigungService();
        var kasse = new KassenService(factory, umsatz);
        var sitzung = (await kasse.StarteSitzungAsync(salon.Id, awa.Id, 5000)).Sitzung!;
        return new Basis(factory, salon.Id, besitzer.Id, awa.Id, kofi.Id, fremd.Id, artikel, kasse, new StornoService(factory, kasse, umsatz), sitzung.Id);
    }

    private static async Task<Verkauf> BucheAsync(Basis b, Guid? kassierer, int menge = 1, ZahlungsArt art = ZahlungsArt.Bar)
    {
        var (ok, fehler, verkauf, _) = await b.Kasse.BucheVerkaufAsync(b.Sitzung,
            [new KassenService.VerkaufsPositionInput(b.Artikel.Id, menge, null, false)],
            [new KassenService.ZahlungInput(art, Gegeben: art == ZahlungsArt.Bar ? menge * 1000m : null)],
            kassiererId: kassierer);
        Assert.True(ok, fehler);
        return verkauf!;
    }

    [Fact]
    public async Task Jeder_Bon_Gehoert_Der_Angemeldeten_Person_Ohne_Angabe_Der_Person_Die_Geoeffnet_Hat()
    {
        var b = await BasisAsync();
        var vonKofi = await BucheAsync(b, b.Kofi);
        var ohneAngabe = await BucheAsync(b, null);

        await using var db = await b.F.CreateDbContextAsync();
        var k = await db.Verkaeufe.SingleAsync(v => v.Id == vonKofi.Id);
        Assert.Equal((b.Kofi, "Kofi"), (k.KassiererId!.Value, k.KassiererName));
        var o = await db.Verkaeufe.SingleAsync(v => v.Id == ohneAngabe.Id);
        Assert.Equal((b.Awa, "Awa"), (o.KassiererId!.Value, o.KassiererName));
        // Alte Bons ohne Kassierer zeigen die Person der Sitzung.
        Assert.Equal("Awa", new Verkauf().KassiererAnzeige("Awa"));
    }

    [Fact]
    public async Task Person_Eines_Anderen_Ladens_Oder_Gesperrt_Bucht_Nichts()
    {
        var b = await BasisAsync();
        var (ok, fehler, _, _) = await b.Kasse.BucheVerkaufAsync(b.Sitzung,
            [new KassenService.VerkaufsPositionInput(b.Artikel.Id, 1, null, false)], [new KassenService.ZahlungInput(ZahlungsArt.Bar, Gegeben: 1000)],
            kassiererId: b.Fremd);
        Assert.False(ok);
        Assert.Equal(KassenService.KassiererUnbekanntFehler, fehler);

        await using (var db = await b.F.CreateDbContextAsync())
        {
            (await db.Users.SingleAsync(u => u.Id == b.Kofi)).IsBlocked = true;
            await db.SaveChangesAsync();
        }
        (ok, fehler, _, _) = await b.Kasse.BucheVerkaufAsync(b.Sitzung,
            [new KassenService.VerkaufsPositionInput(b.Artikel.Id, 1, null, false)], [new KassenService.ZahlungInput(ZahlungsArt.Bar, Gegeben: 1000)],
            kassiererId: b.Kofi);
        Assert.False(ok);
        await using var pruefung = await b.F.CreateDbContextAsync();
        Assert.Empty(pruefung.Verkaeufe);
    }

    [Fact]
    public async Task PIN_Pruefung_Nur_Fuer_Personen_Dieses_Ladens()
    {
        var b = await BasisAsync();
        var ok = await b.Kasse.PruefeKassiererAsync(b.Salon, b.Kofi, "3333");
        Assert.True(ok.Erfolg);
        Assert.Equal("Kofi", ok.Name);
        Assert.False((await b.Kasse.PruefeKassiererAsync(b.Salon, b.Kofi, "9999")).Erfolg);
        // Richtige PIN, aber die Person gehört zu einem anderen Laden.
        Assert.False((await b.Kasse.PruefeKassiererAsync(b.Salon, b.Fremd, "4444")).Erfolg);
        // Besitzer kassiert mit seiner Besitzer-PIN.
        Assert.Equal("Ama Propriétaire", (await b.Kasse.PruefeKassiererAsync(b.Salon, b.Besitzer, "1234")).Name);
    }

    [Fact]
    public async Task Clôture_Zeigt_Bons_Und_Bargeld_Je_Person_Netto_Nach_Erstattung()
    {
        var b = await BasisAsync();
        await BucheAsync(b, b.Awa, 2);                       // Awa: 2.000 bar
        var kofiBar = await BucheAsync(b, b.Kofi, 3);        // Kofi: 3.000 bar
        await BucheAsync(b, b.Kofi, 1, ZahlungsArt.Karte);   // Kofi: 1.000 Karte

        // Kofi erstattet 1 Stück seines Barverkaufs selbst (hat das Recht « Remboursements », keine Besitzer-PIN nötig).
        var posId = kofiBar.Positionen.Single().Id;
        var storno = await b.Storno.ErstatteAsync(b.Besitzer, b.Sitzung, kofiBar.Id, [new(posId, 1)], "0:", true, "Défaut", null, kassiererId: b.Kofi);
        Assert.True(storno.Erfolg, storno.Fehler);
        Assert.Equal("Kofi", storno.Storno!.KassiererName);
        Assert.Equal("Kofi", storno.Storno.StornoErfasstVonName);

        var u = (await b.Kasse.GetSitzungsUebersichtAsync(b.Sitzung))!;
        var personen = u.NachKassierer!;
        Assert.Equal(2, personen.Count);
        var kofi = personen.Single(p => p.Name == "Kofi");
        Assert.Equal((2, 1, 2000m, 1000m), (kofi.Verkaeufe, kofi.Erstattungen, kofi.Bar, kofi.Karte));
        var awa = personen.Single(p => p.Name == "Awa");
        Assert.Equal((1, 2000m), (awa.Verkaeufe, awa.Bar));
        // Summe je Person = Bareinnahmen der Kasse.
        Assert.Equal(u.Bar, personen.Sum(p => p.Bar));

        // Finanzen › Clôtures: Aufstellung erscheint, weil zwei Personen kassiert haben.
        var anteile = await new FinanzService(b.F).GetAbschlussAnteileAsync([b.Sitzung]);
        Assert.Equal(2, anteile[b.Sitzung].Count);
    }

    [Fact]
    public async Task Recht_Remboursements_Haengt_An_Der_Angemeldeten_Person()
    {
        var b = await BasisAsync();
        var verkauf = await BucheAsync(b, b.Awa, 2);
        var posId = verkauf.Positionen.Single().Id;
        // Awa hat das Recht nicht → ohne Besitzer-PIN abgelehnt …
        Assert.False((await b.Storno.ErstatteAsync(b.Besitzer, b.Sitzung, verkauf.Id, [new(posId, 1)], "0:", true, "Défaut", null, kassiererId: b.Awa)).Erfolg);
        // … Kofi (mit Recht) am selben gemeinsamen Gerät darf.
        Assert.True((await b.Storno.ErstatteAsync(b.Besitzer, b.Sitzung, verkauf.Id, [new(posId, 1)], "0:", true, "Défaut", null, kassiererId: b.Kofi)).Erfolg);
    }

    [Fact]
    public void Geraet_Merkt_Sich_Die_Person_Bis_Zur_Neuen_Sitzung()
    {
        var state = new KassenAnzeigeState(new BesitzerPinState());
        var sitzung = Guid.NewGuid();
        var awa = Guid.NewGuid();
        state.SitzungGestartet(sitzung, Guid.NewGuid(), "Awa", kassiererId: awa);
        Assert.False(state.KassiererFehlt);

        var kofi = Guid.NewGuid();
        state.SetzeKassierer(kofi, "Kofi");
        // Zurück in die Kasse (gleiche Sitzung, ohne PIN): Kofi bleibt angemeldet.
        state.SitzungGestartet(sitzung, Guid.NewGuid(), "Awa");
        Assert.Equal((kofi, "Kofi"), (state.AktiverKassiererId!.Value, state.KassiererName));

        // Kasse anderswo geschlossen und neu geöffnet: neue Sitzung → neu fragen.
        state.SitzungGestartet(Guid.NewGuid(), Guid.NewGuid(), "Awa");
        Assert.True(state.KassiererFehlt);
        state.SitzungBeendet();
        Assert.Null(state.AktiverKassiererId);
    }
}
