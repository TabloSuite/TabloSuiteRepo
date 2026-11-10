using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

/// <summary>Kundenkartei: Anlage mit/ohne Telefon (nur mit Zustimmung), Suche, Kennzahlen, Rechte, Löschen.</summary>
public class KundenServiceTests
{
    private sealed record Welt(IDbContextFactory<ApplicationDbContext> F, Salon Laden, Salon Fremd, Guid Besitzer, Guid Kassierer, Guid Fremder, KassenSitzung Sitzung);

    private static async Task<Welt> BasisAsync()
    {
        var f = TestDbContextFactory.Create();
        var laden = new Salon { Name = "Salon", IstAktiv = true, IstFreigegeben = true };
        var fremd = new Salon { Name = "Autre", IstAktiv = true, IstFreigegeben = true };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Ama" };
        var kassierer = new AppUser { Id = Guid.NewGuid(), FullName = "Awa" };
        var fremder = new AppUser { Id = Guid.NewGuid(), FullName = "Kofi" };
        await using var db = await f.CreateDbContextAsync();
        var rolle = new Role { Name = AppRollen.Besitzer };
        var kRolle = new Role { Name = AppRollen.Kassierer };
        db.Roles.AddRange(rolle, kRolle);
        db.UserRoles.AddRange(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id }, new AppUserRole { UserId = kassierer.Id, RoleId = kRolle.Id },
            new AppUserRole { UserId = fremder.Id, RoleId = rolle.Id });
        db.UserSalons.AddRange(new UserSalon { AppUser = besitzer, Salon = laden }, new UserSalon { AppUser = kassierer, Salon = laden },
            new UserSalon { AppUser = fremder, Salon = fremd });
        var sitzung = new KassenSitzung { Salon = laden, KassiererId = kassierer.Id, Status = KassenSitzungStatus.Offen };
        db.KassenSitzungen.Add(sitzung);
        await db.SaveChangesAsync();
        return new Welt(f, laden, fremd, besitzer.Id, kassierer.Id, fremder.Id, sitzung);
    }

    private static async Task VerkaufAsync(Welt w, Guid? kunde, decimal betrag, DateTime zeit, string leistung, Guid? stornoVon = null)
    {
        await using var db = await w.F.CreateDbContextAsync();
        var v = new Verkauf { KassenSitzungId = w.Sitzung.Id, SalonId = w.Laden.Id, KundeId = kunde, GesamtSumme = betrag, ZeitUtc = zeit, StornoVonVerkaufId = stornoVon };
        v.Positionen.Add(new VerkaufsPosition { LeistungName = leistung, Menge = betrag < 0 ? -1 : 1, ZeilenSumme = betrag });
        db.Verkaeufe.Add(v);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Telefon_Nur_Mit_Zustimmung_Und_Ohne_Dubletten()
    {
        var w = await BasisAsync();
        var s = new KundenService(w.F, TimeProvider.System);
        var ohne = await s.LegeAnAsync(w.Kassierer, w.Laden.Id, "Yawa Lawson", null, false);
        Assert.Null(ohne.TelefonGekuerzt);

        await Assert.ThrowsAsync<ArgumentException>(() => s.LegeAnAsync(w.Kassierer, w.Laden.Id, "Edem", "+228 90 11 22 33", einwilligung: false));
        var mit = await s.LegeAnAsync(w.Kassierer, w.Laden.Id, "Edem Agbeko", "+228 90 11 22 33", true);
        Assert.Equal("•• 22 33", mit.TelefonGekuerzt);
        var dublette = await Assert.ThrowsAsync<ArgumentException>(() => s.LegeAnAsync(w.Besitzer, w.Laden.Id, "Autre", "22890112233", true));
        Assert.Contains("Edem Agbeko", dublette.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => s.LegeAnAsync(w.Kassierer, w.Laden.Id, "X", null, false));
        await Assert.ThrowsAsync<ArgumentException>(() => s.LegeAnAsync(w.Kassierer, w.Laden.Id, "Komla", "abc123", true));

        await using var db = await w.F.CreateDbContextAsync();
        var gespeichert = await db.Kunden.SingleAsync(k => k.Id == mit.Id);
        Assert.Equal("22890112233", gespeichert.TelefonZiffern);
        Assert.NotNull(gespeichert.TelefonEinwilligungUtc);
    }

    [Fact]
    public async Task Suche_Nach_Name_Und_Nummer_Nur_Im_Eigenen_Laden()
    {
        var w = await BasisAsync();
        var s = new KundenService(w.F, TimeProvider.System);
        await s.LegeAnAsync(w.Kassierer, w.Laden.Id, "Akossiwa Mensah", "+228 91 45 67 12", true);
        await s.LegeAnAsync(w.Kassierer, w.Laden.Id, "Kossi Amegah", null, false);
        Assert.Equal(2, (await s.SucheAsync(w.Kassierer, w.Laden.Id, "kossi")).Count);
        Assert.Equal("Akossiwa Mensah", Assert.Single(await s.SucheAsync(w.Kassierer, w.Laden.Id, "4567")).Name);
        // Andere Läden: kein Zugriff
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.SucheAsync(w.Fremder, w.Laden.Id, "kossi"));
        // Kassierer darf anlegen und suchen, aber keine Liste mit vollen Nummern
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.GetListeAsync(w.Kassierer, w.Laden.Id, null));
    }

    [Fact]
    public async Task Besuche_Und_Umsatz_Ohne_Rueckerstattungen_Und_Loeschen_Behaelt_Verkaeufe()
    {
        var w = await BasisAsync();
        var s = new KundenService(w.F, TimeProvider.System);
        var k = await s.LegeAnAsync(w.Kassierer, w.Laden.Id, "Ama Kodjo", null, false);
        var heute = DateTime.UtcNow;
        await VerkaufAsync(w, k.Id, 15000, heute.AddDays(-10), "Tresses africaines");
        await VerkaufAsync(w, k.Id, 2500, heute.AddDays(-2), "Shampoing + soin");
        await VerkaufAsync(w, k.Id, -2500, heute.AddDays(-1), "Shampoing + soin", stornoVon: Guid.NewGuid());
        await VerkaufAsync(w, null, 3000, heute, "Brushing");

        var zeile = Assert.Single(await s.GetListeAsync(w.Besitzer, w.Laden.Id, null));
        Assert.Equal(2, zeile.Besuche);
        Assert.Equal(15000m, zeile.Umsatz);
        Assert.Equal(2, Assert.Single(await s.SucheAsync(w.Kassierer, w.Laden.Id, "ama")).Besuche);

        var detail = await s.GetDetailAsync(w.Besitzer, k.Id);
        Assert.Equal("Tresses africaines", detail.Lieblinge[0].Leistung);
        Assert.Equal(3, detail.LetzteBesuche.Count);
        Assert.True(detail.LetzteBesuche[0].IstRueckerstattung);

        var zahlen = await s.GetKennzahlenAsync(w.Besitzer, w.Laden.Id);
        Assert.Equal(1, zahlen.Kunden);
        Assert.Equal(1, zahlen.Wiederkehrend);

        await s.AktualisiereAsync(w.Besitzer, k.Id, "Ama Kodjo", null, false, "Allergique au henné");
        Assert.Equal("Allergique au henné", (await s.GetDetailAsync(w.Besitzer, k.Id)).Notiz);

        await s.LoescheAsync(w.Besitzer, k.Id);
        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(4, await db.Verkaeufe.CountAsync());
        Assert.False(await db.Verkaeufe.AnyAsync(v => v.KundeId != null));
        Assert.False(await db.Kunden.AnyAsync());
    }
}
