using ENote.Core.DataModels.Finanzen;
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

public class BestandsServiceTests
{
    private sealed record Basis(IDbContextFactory<ApplicationDbContext> F, Salon Salon, AppUser Besitzer, AppUser Kassierer,
        Leistung Huile, KassenService Kasse, BestandsService Service, Guid SitzungId);

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

        var kasse = new KassenService(factory, new FakeUmsatzBenachrichtigungService());
        var sitzung = (await kasse.StarteSitzungAsync(salon.Id, kassierer.Id, 10000)).Sitzung!;
        return new Basis(factory, salon, besitzer, kassierer, huile, kasse, new BestandsService(factory, kasse), sitzung.Id);
    }

    [Fact]
    public async Task Abschreibung_Mindert_Bestand_Mit_Einkaufswert_Und_Prueft_Rechte_Und_Menge()
    {
        var b = await BasisAsync();

        Assert.False((await b.Service.SchreibeAbAsync(b.Kassierer.Id, b.Huile.Id, 1, AbschreibungsGrund.Casse, null)).Erfolg); // kein Recht
        Assert.False((await b.Service.SchreibeAbAsync(b.Besitzer.Id, b.Huile.Id, 11, AbschreibungsGrund.Casse, null)).Erfolg); // > Bestand
        Assert.False((await b.Service.SchreibeAbAsync(b.Besitzer.Id, b.Huile.Id, 1, AbschreibungsGrund.Autre, " ")).Erfolg);   // Grund fehlt
        Assert.True((await b.Service.SchreibeAbAsync(b.Besitzer.Id, b.Huile.Id, 2, AbschreibungsGrund.Casse, "Flacon tombé")).Erfolg);

        await using var db = await b.F.CreateDbContextAsync();
        Assert.Equal(8m, (await db.Leistungen.SingleAsync(l => l.Id == b.Huile.Id)).Stueckzahl);
        var abgang = await db.LagerAbgaenge.SingleAsync();
        Assert.Equal((LagerAbgangArt.Abschreibung, 3600m), (abgang.Art, abgang.Einkaufswert!.Value));
    }

    [Fact]
    public async Task Entnahmen_An_Der_Kasse_Brauchen_Besitzer_Pin_Und_Wirken_Auf_Bestand_Und_Kasse()
    {
        var b = await BasisAsync();

        // Falsche/fehlende PIN → abgelehnt (der Kassierer bedient die Kasse)
        Assert.False((await b.Service.EntnehmeBargeldAsync(b.Kassierer.Id, b.SitzungId, null, 2000, null)).Erfolg);
        Assert.False((await b.Service.EntnehmeBargeldAsync(b.Kassierer.Id, b.SitzungId, "9999", 2000, null)).Erfolg);

        // Nicht mehr entnehmen als erwartet in der Kasse liegt
        Assert.False((await b.Service.EntnehmeBargeldAsync(b.Kassierer.Id, b.SitzungId, "1234", 50000, null)).Erfolg);

        Assert.True((await b.Service.EntnehmeBargeldAsync(b.Kassierer.Id, b.SitzungId, "1234", 2000, "Transport")).Erfolg);
        Assert.True((await b.Service.EntnehmeWareAsync(b.Kassierer.Id, b.SitzungId, "1234", b.Huile.Id, 1, "Usage perso")).Erfolg);
        Assert.Equal(8000m, await b.Kasse.GetBarSollAsync(b.SitzungId)); // 10.000 − 2.000

        // Einlage in die Kasse (Besitzer) erhöht den Bestand; ohne Provenance abgelehnt
        Assert.False((await b.Service.ErfasseEinlageAsync(b.Besitzer.Id, b.Salon.Id, 5000, GeldOrt.Kasse, null, null)).Erfolg);
        Assert.False((await b.Service.ErfasseEinlageAsync(b.Kassierer.Id, b.Salon.Id, 5000, GeldOrt.Kasse, "Aide", null)).Erfolg);
        Assert.True((await b.Service.ErfasseEinlageAsync(b.Besitzer.Id, b.Salon.Id, 5000, GeldOrt.Kasse, "Aide familiale", null)).Erfolg);
        Assert.True((await b.Service.ErfasseEinlageAsync(b.Besitzer.Id, b.Salon.Id, 20000, GeldOrt.AusserhalbKasse, "Microcrédit", "Flooz")).Erfolg);
        Assert.Equal(13000m, await b.Kasse.GetBarSollAsync(b.SitzungId)); // + 5.000 in die Kasse, Mobile Money nicht

        var uebersicht = await b.Kasse.GetSitzungsUebersichtAsync(b.SitzungId);
        Assert.Equal((5000m, 2000m, 13000m), (uebersicht!.EinlagenBar, uebersicht.EntnahmenBar, uebersicht.BarSoll));

        // Abschluss vergleicht mit dem Soll inkl. Bewegungen
        var (_, _, abgeschlossen) = await b.Kasse.SchliesseSitzungAsync(b.SitzungId, 13000, null);
        Assert.Equal(0m, abgeschlossen!.Differenz);

        // Finanzbuch: Saldo = Ventes + Apports − Dépenses − Retraits; Warenentnahme nur informativ
        var finanz = await new FinanzService(b.F).GetFinanzUebersichtAsync(b.Salon.Id, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        Assert.Equal((25000m, 2000m, 1800m), (finanz.Einlagen, finanz.EntnahmenBar, finanz.WarenEntnahmenWert));
        Assert.Equal(23000m, finanz.Saldo);

        await using var db = await b.F.CreateDbContextAsync();
        Assert.Equal(9m, (await db.Leistungen.SingleAsync(l => l.Id == b.Huile.Id)).Stueckzahl);
    }

    /// <summary>16.11.2026 (Nutzerwunsch): Ein Écart bei der Clôture meldet sich beim Besitzer (Hinweis auf der Startseite,
    /// mit Soll/Ist und Notiz); ohne Écart kein Hinweis.</summary>
    [Fact]
    public async Task Ecart_Bei_Der_Cloture_Meldet_Sich_Beim_Besitzer()
    {
        var mit = await BasisAsync();
        var soll = (await mit.Kasse.GetBarSollAsync(mit.SitzungId))!.Value;
        var (_, _, s) = await mit.Kasse.SchliesseSitzungAsync(mit.SitzungId, soll - 1000m, "  billet abîmé ");
        Assert.Equal(-1000m, s!.Differenz);
        await using (var db = await mit.F.CreateDbContextAsync())
        {
            var h = await db.BesitzerHinweise.SingleAsync();
            Assert.Equal(mit.Salon.Id, h.SalonId);
            Assert.True(h.IstUngelesen);
            Assert.Contains("il manque", h.Text);
            Assert.Contains("Note : « billet abîmé »", h.Text);
        }

        var ohne = await BasisAsync();
        await ohne.Kasse.SchliesseSitzungAsync(ohne.SitzungId, (await ohne.Kasse.GetBarSollAsync(ohne.SitzungId))!.Value, null);
        await using (var db = await ohne.F.CreateDbContextAsync())
        {
            Assert.Empty(await db.BesitzerHinweise.ToListAsync());
        }

        Assert.Contains("de plus que prévu", KassenService.EcartHinweis(500m, 10000m, 10500m, null));
        Assert.DoesNotContain("Note", KassenService.EcartHinweis(500m, 10000m, 10500m, "   "));
    }

    [Fact]
    public async Task Einlage_In_Die_Kasse_Ohne_Offene_Sitzung_Wird_Abgelehnt()
    {
        var b = await BasisAsync();
        await b.Kasse.SchliesseSitzungAsync(b.SitzungId, 10000, null);

        var ergebnis = await b.Service.ErfasseEinlageAsync(b.Besitzer.Id, b.Salon.Id, 5000, GeldOrt.Kasse, "Aide", null);

        Assert.False(ergebnis.Erfolg);
        Assert.Contains("hors caisse", ergebnis.Fehler);
    }
}
