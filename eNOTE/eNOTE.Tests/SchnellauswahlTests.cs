using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

/// <summary>Schnellauswahl « Nouvelle arrivée »: Motive aus dem Katalog, Räume vom Besitzer (Paramètres › Salles et postes).</summary>
public class SchnellauswahlTests
{
    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(jetzt, DateTimeKind.Utc));
    }

    private sealed record Welt(IDbContextFactory<ApplicationDbContext> F, Guid Laden, Guid Besitzer, Guid Kassierer, Guid Fremder,
        KundenBesuchService Besuche, Guid Kunde, Guid Sitzung, DateTime Jetzt);

    private static async Task<Welt> BasisAsync()
    {
        var f = TestDbContextFactory.Create();
        var laden = new Salon { Name = "Cabinet Espoir", IstAktiv = true, IstFreigegeben = true };
        var fremd = new Salon { Name = "Autre", IstAktiv = true, IstFreigegeben = true };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Ama Mensah" };
        var kassierer = new AppUser { Id = Guid.NewGuid(), FullName = "Awa Koffi" };
        var fremder = new AppUser { Id = Guid.NewGuid(), FullName = "Kofi" };
        var kunde = new Kunde { SalonId = laden.Id, Name = "Akossiwa Ablo" };
        var sitzung = new KassenSitzung { SalonId = laden.Id, KassiererId = kassierer.Id, Status = KassenSitzungStatus.Offen };
        await using (var db = await f.CreateDbContextAsync())
        {
            var rB = new Role { Name = AppRollen.Besitzer };
            var rK = new Role { Name = AppRollen.Kassierer };
            db.Roles.AddRange(rB, rK);
            db.Users.AddRange(besitzer, kassierer, fremder);
            db.UserRoles.AddRange(new AppUserRole { UserId = besitzer.Id, RoleId = rB.Id }, new AppUserRole { UserId = kassierer.Id, RoleId = rK.Id },
                new AppUserRole { UserId = fremder.Id, RoleId = rB.Id });
            db.Salons.AddRange(laden, fremd);
            db.UserSalons.AddRange(new UserSalon { AppUserId = besitzer.Id, SalonId = laden.Id }, new UserSalon { AppUserId = kassierer.Id, SalonId = laden.Id },
                new UserSalon { AppUserId = fremder.Id, SalonId = fremd.Id });
            db.Kunden.Add(kunde);
            db.KassenSitzungen.Add(sitzung);
            await db.SaveChangesAsync();
        }
        var jetzt = new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);
        return new Welt(f, laden.Id, besitzer.Id, kassierer.Id, fremder.Id, new KundenBesuchService(f, new Uhr(jetzt), new AnwesenheitsSignal()),
            kunde.Id, sitzung.Id, jetzt);
    }

    private static async Task<Guid> LeistungAsync(Welt w, string name, LeistungsTyp typ = LeistungsTyp.Prestation, bool aktiv = true, bool standard = false)
    {
        await using var db = await w.F.CreateDbContextAsync();
        var l = new Leistung { SalonId = w.Laden, Name = name, Preis = 1000m, Typ = typ, IstAktiv = aktiv, IstStandardArtikel = standard };
        db.Leistungen.Add(l);
        await db.SaveChangesAsync();
        return l.Id;
    }

    private static async Task VerkaufAsync(Welt w, Guid leistung, int menge, int tageZurueck = 1)
    {
        await using var db = await w.F.CreateDbContextAsync();
        var v = new Verkauf { SalonId = w.Laden, KassenSitzungId = w.Sitzung, ZeitUtc = w.Jetzt.AddDays(-tageZurueck) };
        v.Positionen.Add(new VerkaufsPosition { LeistungId = leistung, Menge = menge, LeistungName = "x" });
        db.Verkaeufe.Add(v);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Ohne_Katalog_Und_Raeume_Gelernt_Aus_Den_Besuchen()
    {
        var w = await BasisAsync();
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.KundenBesuche.AddRange(
                new KundenBesuch { SalonId = w.Laden, KundeId = w.Kunde, Grund = "Consultation", Raum = "Salle 2", AnkunftUtc = w.Jetzt.AddDays(-2), EndeUtc = w.Jetzt.AddDays(-2) },
                new KundenBesuch { SalonId = w.Laden, KundeId = w.Kunde, Grund = "Consultation", Raum = "Salle 1", AnkunftUtc = w.Jetzt.AddDays(-3), EndeUtc = w.Jetzt.AddDays(-3) },
                new KundenBesuch { SalonId = w.Laden, KundeId = w.Kunde, Grund = "Pansement", Raum = "Salle 2", AnkunftUtc = w.Jetzt.AddDays(-4), EndeUtc = w.Jetzt.AddDays(-4) });
            await db.SaveChangesAsync();
        }
        var v = await w.Besuche.GetVorschlaegeAsync(w.Kassierer, w.Laden);
        Assert.Equal(["Consultation", "Pansement"], v.Gruende);
        Assert.Equal(["Salle 2", "Salle 1"], v.Raeume);
    }

    [Fact]
    public async Task Motive_Aus_Dem_Katalog_Favoriten_Zuerst_Dann_Meistverkauft()
    {
        var w = await BasisAsync();
        var brushing = await LeistungAsync(w, "Brushing");
        var tresses = await LeistungAsync(w, "Tresses africaines");
        var coupe = await LeistungAsync(w, "Coupe homme");
        await LeistungAsync(w, "Défrisage");
        await LeistungAsync(w, "Beurre de karité", LeistungsTyp.Produit);       // Produkt: kein Motiv
        await LeistungAsync(w, "Ancienne coupe", aktiv: false);                 // inaktiv
        await LeistungAsync(w, "Divers", standard: true);                       // freier Betrag
        for (var i = 0; i < 6; i++) await LeistungAsync(w, $"Soin {i}");
        await VerkaufAsync(w, coupe, 5);
        await VerkaufAsync(w, brushing, 2);
        await VerkaufAsync(w, brushing, 9, tageZurueck: 120);                   // zu alt: zählt nicht
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.LeistungFavoriten.Add(new LeistungFavorit { SalonId = w.Laden, LeistungId = tresses, GesetztVonId = w.Besitzer });
            await db.SaveChangesAsync();
        }

        var v = await w.Besuche.GetVorschlaegeAsync(w.Kassierer, w.Laden);
        Assert.Equal(8, v.Gruende.Count);
        Assert.Equal(["Tresses africaines", "Coupe homme", "Brushing", "Défrisage", "Soin 0", "Soin 1", "Soin 2", "Soin 3"], v.Gruende);
        // Autovervollständigung: alle Prestations (ohne Produkte, Inaktive, « Divers »), alphabetisch
        Assert.Equal(["Brushing", "Coupe homme", "Défrisage", "Soin 0", "Soin 1", "Soin 2", "Soin 3", "Soin 4", "Soin 5", "Tresses africaines"], v.Katalog);
    }

    [Fact]
    public async Task Besitzer_Legt_Raeume_An_Benennt_Um_Sortiert_Und_Loescht()
    {
        var w = await BasisAsync();
        var s1 = await w.Besuche.RaumAnlegenAsync(w.Besitzer, w.Laden, "  Salle   1 ");
        var bac = await w.Besuche.RaumAnlegenAsync(w.Besitzer, w.Laden, "Bac");
        await w.Besuche.RaumAnlegenAsync(w.Besitzer, w.Laden, "Cabine 3");
        Assert.Equal("Salle 1", s1.Name);

        var doppelt = await Assert.ThrowsAsync<ArgumentException>(() => w.Besuche.RaumAnlegenAsync(w.Besitzer, w.Laden, "bac"));
        Assert.Contains("existe déjà", doppelt.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => w.Besuche.RaumAnlegenAsync(w.Besitzer, w.Laden, "   "));
        await Assert.ThrowsAsync<ArgumentException>(() => w.Besuche.RaumAnlegenAsync(w.Besitzer, w.Laden, new string('x', 41)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Besuche.RaumAnlegenAsync(w.Kassierer, w.Laden, "Salle 9"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Besuche.RaumAnlegenAsync(w.Fremder, w.Laden, "Salle 9"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Besuche.RaumLoeschenAsync(w.Kassierer, bac.Id));

        await w.Besuche.RaumUmbenennenAsync(w.Besitzer, s1.Id, "Salle 2");
        await Assert.ThrowsAsync<ArgumentException>(() => w.Besuche.RaumUmbenennenAsync(w.Besitzer, s1.Id, "Cabine 3"));
        await w.Besuche.RaumVerschiebenAsync(w.Besitzer, bac.Id, -1);            // Bac nach oben
        Assert.Equal(["Bac", "Salle 2", "Cabine 3"], (await w.Besuche.GetRaeumeAsync(w.Kassierer, w.Laden)).Select(r => r.Name));

        // Gelernte Räume treten zurück, solange eigene festgelegt sind
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.KundenBesuche.Add(new KundenBesuch { SalonId = w.Laden, KundeId = w.Kunde, Grund = "Consultation", Raum = "Poste2", AnkunftUtc = w.Jetzt.AddDays(-1), EndeUtc = w.Jetzt.AddDays(-1) });
            await db.SaveChangesAsync();
        }
        Assert.Equal(["Bac", "Salle 2", "Cabine 3"], (await w.Besuche.GetVorschlaegeAsync(w.Kassierer, w.Laden)).Raeume);

        foreach (var r in await w.Besuche.GetRaeumeAsync(w.Besitzer, w.Laden)) await w.Besuche.RaumLoeschenAsync(w.Besitzer, r.Id);
        Assert.Equal(["Poste2"], (await w.Besuche.GetVorschlaegeAsync(w.Kassierer, w.Laden)).Raeume);
    }
}
