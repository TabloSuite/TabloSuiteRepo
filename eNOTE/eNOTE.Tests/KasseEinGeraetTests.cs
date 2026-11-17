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
/// Nutzerentscheidung 17.11.2026: eine Kasse je Laden, an genau einem Gerät. Ein zweites Gerät kassiert nicht in der
/// Kasse eines anderen Geräts mit, sondern kann sie nur mit Besitzer-PIN übernehmen (« Reprendre la caisse ici »);
/// das bisherige Gerät bucht danach nichts mehr.
/// </summary>
public class KasseEinGeraetTests
{
    private const string Tablet = "0123456789abcdef0123456789abcdef";
    private const string Handy = "fedcba9876543210fedcba9876543210";

    private sealed record Basis(IDbContextFactory<ApplicationDbContext> F, Guid Salon, Guid Besitzer, Guid Awa, Leistung Artikel,
        KassenService Kasse, AnwesenheitsSignal Signal, Guid Sitzung);

    private static async Task<Basis> BasisAsync(bool mitBesitzerPin = true, string? geraet = Tablet)
    {
        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Name = "Boutique", IstAktiv = true, IstFreigegeben = true };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Ama Propriétaire" };
        var awa = new AppUser { Id = Guid.NewGuid(), FullName = "Awa" };
        var artikel = new Leistung { SalonId = salon.Id, Name = "Savon", Preis = 1000, IstAktiv = true };
        await using (var db = await factory.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.Besitzer, NormalizedName = "BESITZER" };
            db.Roles.Add(rolle);
            db.Users.AddRange(besitzer, awa);
            db.UserRoles.Add(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id });
            db.Salons.Add(salon);
            db.UserSalons.AddRange(
                new UserSalon { AppUserId = besitzer.Id, SalonId = salon.Id, BesitzerPinHash = mitBesitzerPin ? KassenPinService.HashPin("1234") : null,
                    KassenPinHash = mitBesitzerPin ? null : KassenPinService.HashPin("1111") },
                new UserSalon { AppUserId = awa.Id, SalonId = salon.Id, KassenPinHash = KassenPinService.HashPin("2222") });
            db.Leistungen.Add(artikel);
            await db.SaveChangesAsync();
        }
        var signal = new AnwesenheitsSignal();
        var kasse = new KassenService(factory, new FakeUmsatzBenachrichtigungService(), anwesenheit: signal);
        var sitzung = (await kasse.StarteSitzungAsync(salon.Id, awa.Id, 5000, geraet: geraet)).Sitzung!;
        return new Basis(factory, salon.Id, besitzer.Id, awa.Id, artikel, kasse, signal, sitzung.Id);
    }

    private static Task<(bool Erfolg, string? Fehler, Verkauf? Verkauf, List<string> Warnungen)> BucheAsync(Basis b, string? geraet)
        => b.Kasse.BucheVerkaufAsync(b.Sitzung,
            [new KassenService.VerkaufsPositionInput(b.Artikel.Id, 1, null, false)],
            [new KassenService.ZahlungInput(ZahlungsArt.Bar, Gegeben: 1000m)],
            kassiererId: b.Awa, geraet: geraet);

    [Fact]
    public async Task Kasse_Merkt_Sich_Das_Geraet_Das_Sie_Oeffnet()
    {
        var b = await BasisAsync();
        await using var db = await b.F.CreateDbContextAsync();
        Assert.Equal(Tablet, (await db.KassenSitzungen.SingleAsync(k => k.Id == b.Sitzung)).GeraetSchluessel);
        Assert.Equal(KassenService.GeraetStatus.DiesesGeraet, await b.Kasse.PruefeGeraetAsync(b.Sitzung, Tablet));
        Assert.Equal(KassenService.GeraetStatus.AnderesGeraet, await b.Kasse.PruefeGeraetAsync(b.Sitzung, Handy));
    }

    [Fact]
    public async Task Zweites_Geraet_Kann_Weder_Verkaufen_Noch_Abschliessen()
    {
        var b = await BasisAsync();
        var (ok, fehler, _, _) = await BucheAsync(b, Handy);
        Assert.False(ok);
        Assert.Equal(KassenService.AnderesGeraetFehler, fehler);

        var (zu, zuFehler, _) = await b.Kasse.SchliesseSitzungAsync(b.Sitzung, 5000, null, geraet: Handy);
        Assert.False(zu);
        Assert.Equal(KassenService.AnderesGeraetFehler, zuFehler);

        Assert.True((await BucheAsync(b, Tablet)).Erfolg);
        await using var db = await b.F.CreateDbContextAsync();
        Assert.Equal(1, await db.Verkaeufe.CountAsync());
        Assert.Equal(KassenSitzungStatus.Offen, (await db.KassenSitzungen.SingleAsync()).Status);
    }

    [Fact]
    public async Task Uebernahme_Nur_Mit_Besitzer_Pin_Danach_Ist_Das_Alte_Geraet_Gesperrt()
    {
        var b = await BasisAsync();
        Guid? gemeldet = null;
        b.Signal.KassenGeraetGewechselt += s => gemeldet = s;

        Assert.Equal("Code PIN incorrect.", await b.Kasse.UebernehmeKasseAsync(b.Sitzung, Handy, b.Besitzer, "9999"));
        Assert.Equal("Saisissez le code PIN du propriétaire.", await b.Kasse.UebernehmeKasseAsync(b.Sitzung, Handy, b.Besitzer, null));
        Assert.Null(gemeldet);
        Assert.Equal(KassenService.GeraetStatus.DiesesGeraet, await b.Kasse.PruefeGeraetAsync(b.Sitzung, Tablet));

        Assert.Null(await b.Kasse.UebernehmeKasseAsync(b.Sitzung, Handy, b.Besitzer, "1234"));
        Assert.Equal(b.Salon, gemeldet);
        Assert.Equal(KassenService.GeraetStatus.AnderesGeraet, await b.Kasse.PruefeGeraetAsync(b.Sitzung, Tablet));
        Assert.Equal(KassenService.AnderesGeraetFehler, (await BucheAsync(b, Tablet)).Fehler);
        Assert.True((await BucheAsync(b, Handy)).Erfolg);

        // Dieselbe Kasse läuft weiter: Fond und Verkäufe bleiben, die Clôture macht das neue Gerät.
        var (zu, fehler, sitzung) = await b.Kasse.SchliesseSitzungAsync(b.Sitzung, 6000, null, geraet: Handy);
        Assert.True(zu, fehler);
        Assert.Equal(0m, sitzung!.Differenz);
    }

    [Fact]
    public async Task Ohne_Besitzer_Pin_Uebernimmt_Nur_Das_Besitzer_Konto()
    {
        var b = await BasisAsync(mitBesitzerPin: false);
        Assert.Equal("Réservé au propriétaire de l'établissement.", await b.Kasse.UebernehmeKasseAsync(b.Sitzung, Handy, b.Awa, null));
        Assert.Null(await b.Kasse.UebernehmeKasseAsync(b.Sitzung, Handy, b.Besitzer, null));
        Assert.Equal(KassenService.GeraetStatus.DiesesGeraet, await b.Kasse.PruefeGeraetAsync(b.Sitzung, Handy));
    }

    [Fact]
    public async Task Kasse_Von_Vor_Der_Umstellung_Gehoert_Dem_Ersten_Geraet()
    {
        var b = await BasisAsync(geraet: null);
        Assert.Equal(KassenService.GeraetStatus.DiesesGeraet, await b.Kasse.PruefeGeraetAsync(b.Sitzung, Handy));
        Assert.Equal(KassenService.GeraetStatus.AnderesGeraet, await b.Kasse.PruefeGeraetAsync(b.Sitzung, Tablet));
        Assert.Equal(KassenService.AnderesGeraetFehler, (await BucheAsync(b, Tablet)).Fehler);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kein-schluessel")]
    public async Task Ohne_Gueltigen_Geraeteschluessel_Keine_Kasse_Und_Keine_Uebernahme(string? geraet)
    {
        var b = await BasisAsync();
        Assert.Equal(KassenService.GeraetStatus.AnderesGeraet, await b.Kasse.PruefeGeraetAsync(b.Sitzung, geraet));
        Assert.NotNull(await b.Kasse.UebernehmeKasseAsync(b.Sitzung, geraet, b.Besitzer, "1234"));
        Assert.Equal(KassenService.GeraetStatus.DiesesGeraet, await b.Kasse.PruefeGeraetAsync(b.Sitzung, Tablet));
    }

    [Fact]
    public async Task Geschlossene_Kasse_Wird_Erkannt()
    {
        var b = await BasisAsync();
        Assert.True((await b.Kasse.SchliesseSitzungAsync(b.Sitzung, 5000, null, geraet: Tablet)).Erfolg);
        Assert.Equal(KassenService.GeraetStatus.KeineKasse, await b.Kasse.PruefeGeraetAsync(b.Sitzung, Tablet));
        Assert.Equal(KassenService.KeineOffeneKasseFehler, await b.Kasse.UebernehmeKasseAsync(b.Sitzung, Handy, b.Besitzer, "1234"));
    }
}
