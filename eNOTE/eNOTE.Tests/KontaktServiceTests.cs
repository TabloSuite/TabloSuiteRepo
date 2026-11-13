using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>« Aide &amp; contact »: Besitzer sieht seinen Responsable; ohne Responsable das Kontaktformular.</summary>
public class KontaktServiceTests
{
    private sealed record Welt(KontaktService Dienst, Guid Besitzer, Guid Kassierer, Guid Po, Guid SalonId,
        Microsoft.EntityFrameworkCore.IDbContextFactory<ENote.WebApp.Data.ApplicationDbContext> Factory);

    private static async Task<Welt> BaueAsync(bool zuweisung = true, bool abgelaufen = false)
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var salon = new Salon { Name = "Boutique" };
        db.Salons.Add(salon);
        var besitzer = LadenRechteTests.LegeKontoAn(db, "proprio", AppRollen.Besitzer);
        var kassierer = LadenRechteTests.LegeKontoAn(db, "caisse", AppRollen.Kassierer);
        var po = LadenRechteTests.LegeKontoAn(db, "Yao Amegah", AppRollen.ProduktOwner);
        db.UserSalons.Add(new UserSalon { AppUserId = besitzer, SalonId = salon.Id });
        db.UserSalons.Add(new UserSalon { AppUserId = kassierer, SalonId = salon.Id });
        await db.SaveChangesAsync();
        (await db.Users.FindAsync(po))!.TelefonNummer = "+228 90 12 34 56";
        if (zuweisung)
            db.ProduktOwnerSalons.Add(new ProduktOwnerSalon { ProduktOwnerId = po, SalonId = salon.Id,
                GueltigBisUtc = abgelaufen ? DateTime.UtcNow.AddDays(-1) : null });
        await db.SaveChangesAsync();
        return new Welt(new KontaktService(factory), besitzer, kassierer, po, salon.Id, factory);
    }

    [Fact]
    public async Task Besitzer_sieht_Name_Mail_und_Telefon_seines_Responsables()
    {
        var w = await BaueAsync();

        var r = await w.Dienst.GetResponsableAsync(w.Besitzer, w.SalonId);

        Assert.NotNull(r);
        Assert.Equal("Yao Amegah", r.Name);
        Assert.Equal("Yao Amegah@example.test", r.Email);
        Assert.Equal("+228 90 12 34 56", r.Telefon);
        Assert.Equal("https://wa.me/22890123456", r.WhatsAppLink);
        Assert.Equal("tel:+22890123456", r.TelefonLink);
    }

    [Fact]
    public async Task Persoenliche_Betreuung_hat_Vorrang_vor_der_Ladenzuweisung()
    {
        var w = await BaueAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var anderer = LadenRechteTests.LegeKontoAn(db, "Afi Betreuerin", AppRollen.ProduktOwner);
            db.BesitzerBetreuungen.Add(new BesitzerBetreuung { BesitzerId = w.Besitzer, ProduktOwnerId = anderer });
            await db.SaveChangesAsync();
        }

        Assert.Equal("Afi Betreuerin", (await w.Dienst.GetResponsableAsync(w.Besitzer, w.SalonId))?.Name);
    }

    [Fact]
    public async Task Ohne_oder_mit_abgelaufener_Zuweisung_gibt_es_keinen_Responsable()
    {
        Assert.Null(await (await BaueAsync(zuweisung: false)).Dienst.GetResponsableAsync((await BaueAsync(zuweisung: false)).Besitzer, Guid.NewGuid()));
        var w = await BaueAsync(abgelaufen: true);
        Assert.Null(await w.Dienst.GetResponsableAsync(w.Besitzer, w.SalonId));
    }

    [Fact]
    public async Task Mitarbeiter_sehen_keine_Kontaktdaten_des_Responsables()
    {
        var w = await BaueAsync();

        Assert.Null(await w.Dienst.GetResponsableAsync(w.Kassierer, w.SalonId));
    }

    [Fact]
    public void WhatsApp_nur_bei_Nummer_mit_Landesvorwahl()
    {
        Assert.Null(new KontaktService.Responsable("A", null, "90 12 34 56").WhatsAppLink);
        Assert.Equal("https://wa.me/22890123456", new KontaktService.Responsable("A", null, "00228 90 12 34 56").WhatsAppLink);
        Assert.Null(new KontaktService.Responsable("A", null, null).TelefonLink);
    }

    [Theory]
    [InlineData(null, "Bonjour, j'ai une question.", "sujet")]
    [InlineData("Autre demande", "court", "au moins")]
    public async Task Formular_prueft_Sujet_und_Mindestlaenge(string? thema, string nachricht, string erwartet)
    {
        var w = await BaueAsync();

        var fehler = await w.Dienst.SendeAnTabloSuiteAsync(w.Besitzer, w.SalonId, thema, nachricht);

        Assert.NotNull(fehler);
        Assert.Contains(erwartet, fehler);
    }
}
