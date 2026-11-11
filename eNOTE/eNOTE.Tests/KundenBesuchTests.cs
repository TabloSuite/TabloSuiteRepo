using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Kunden;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

/// <summary>« Clients présents »: Ankunft, Raum, Notizen (neueste zuerst), Rechte, Ende beim Encaissement, Löschen.</summary>
public class KundenBesuchTests
{
    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public DateTime Jetzt { get; set; } = jetzt;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    private sealed record Welt(IDbContextFactory<ApplicationDbContext> F, Salon Laden, Guid Besitzer, Guid Kassierer, Guid Fremder,
        KassenSitzung Sitzung, Uhr Uhr, AnwesenheitsSignal Signal, KundenBesuchService Besuche, Guid Kunde);

    private static async Task<Welt> BasisAsync()
    {
        var f = TestDbContextFactory.Create();
        var laden = new Salon { Name = "Cabinet", IstAktiv = true, IstFreigegeben = true };
        var fremd = new Salon { Name = "Autre", IstAktiv = true, IstFreigegeben = true };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Ama Mensah" };
        var kassierer = new AppUser { Id = Guid.NewGuid(), FullName = "Awa Koffi" };
        var fremder = new AppUser { Id = Guid.NewGuid(), FullName = "Kofi" };
        var kunde = new Kunde { SalonId = laden.Id, Name = "Akossiwa Ablo", Telefon = "+228 90 12 34 56", TelefonZiffern = "22890123456" };
        await using (var db = await f.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.Besitzer };
            var kRolle = new Role { Name = AppRollen.Kassierer };
            db.Roles.AddRange(rolle, kRolle);
            db.Users.AddRange(besitzer, kassierer, fremder);
            db.UserRoles.AddRange(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id }, new AppUserRole { UserId = kassierer.Id, RoleId = kRolle.Id },
                new AppUserRole { UserId = fremder.Id, RoleId = rolle.Id });
            db.Salons.AddRange(laden, fremd);
            db.UserSalons.AddRange(new UserSalon { AppUserId = besitzer.Id, SalonId = laden.Id }, new UserSalon { AppUserId = kassierer.Id, SalonId = laden.Id },
                new UserSalon { AppUserId = fremder.Id, SalonId = fremd.Id });
            db.Kunden.Add(kunde);
            await db.SaveChangesAsync();
        }
        KassenSitzung sitzung;
        await using (var db = await f.CreateDbContextAsync())
        {
            sitzung = new KassenSitzung { SalonId = laden.Id, KassiererId = kassierer.Id, Status = KassenSitzungStatus.Offen };
            db.KassenSitzungen.Add(sitzung);
            await db.SaveChangesAsync();
        }
        var uhr = new Uhr(new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc));
        var signal = new AnwesenheitsSignal();
        return new Welt(f, laden, besitzer.Id, kassierer.Id, fremder.Id, sitzung, uhr, signal, new KundenBesuchService(f, uhr, signal), kunde.Id);
    }

    [Fact]
    public async Task Ankunft_Mit_Raum_Und_Notizen_Verlauf_Neueste_Zuerst()
    {
        var w = await BasisAsync();
        var meldungen = 0;
        w.Signal.Geaendert += id => { if (id == w.Laden.Id) meldungen++; };

        var besuchId = await w.Besuche.AnkunftAsync(w.Kassierer, w.Laden.Id, w.Kunde, "  Consultation ", " Salle  2 ", "Tension à prendre");
        var doppelt = await Assert.ThrowsAsync<ArgumentException>(() => w.Besuche.AnkunftAsync(w.Besitzer, w.Laden.Id, w.Kunde, "Pansement", null));
        Assert.Contains("déjà enregistré comme présent", doppelt.Message);

        var anwesend = Assert.Single(await w.Besuche.GetAnwesendeAsync(w.Besitzer, w.Laden.Id));
        Assert.Equal(("Consultation", "Salle 2", "Akossiwa Ablo", "Awa Koffi"), (anwesend.Grund, anwesend.Raum, anwesend.Name, anwesend.ErfasstVon));
        Assert.Equal("Tension à prendre", anwesend.LetzteNotiz);
        Assert.False(anwesend.Vergessen);

        w.Uhr.Jetzt = w.Uhr.Jetzt.AddMinutes(10);
        await w.Besuche.NotizAsync(w.Besitzer, w.Kunde, "Allergie à la pénicilline");           // allgemeine Notiz
        w.Uhr.Jetzt = w.Uhr.Jetzt.AddMinutes(5);
        await w.Besuche.NotizAsync(w.Kassierer, w.Kunde, "Ordonnance remise", besuchId);       // zum Besuch
        await w.Besuche.AendereBesuchAsync(w.Kassierer, besuchId, "Consultation", "Salle 3");

        var fiche = await w.Besuche.GetFicheAsync(w.Kassierer, w.Kunde);
        Assert.Equal("•• 34 56", fiche.Telefon);                                                 // Équipe: Nummer gekürzt
        Assert.Equal("+228 90 12 34 56", (await w.Besuche.GetFicheAsync(w.Besitzer, w.Kunde)).Telefon);
        Assert.Equal("Salle 3", fiche.Offen!.Raum);
        Assert.Equal(["Ordonnance remise", "Tension à prendre"], fiche.Offen.Notizen.Select(n => n.Text));
        // Verlauf: allgemeine Notiz (09:10) vor dem Besuch (09:00)
        Assert.Equal(2, fiche.Verlauf.Count);
        Assert.Equal("Allergie à la pénicilline", fiche.Verlauf[0].Notiz!.Text);
        Assert.Equal(besuchId, fiche.Verlauf[1].Besuch!.Id);
        Assert.True(meldungen >= 3);

        w.Uhr.Jetzt = w.Uhr.Jetzt.AddHours(13);
        Assert.True(Assert.Single(await w.Besuche.GetAnwesendeAsync(w.Kassierer, w.Laden.Id)).Vergessen);
        await w.Besuche.BeendeAsync(w.Kassierer, besuchId);
        Assert.Empty(await w.Besuche.GetAnwesendeAsync(w.Kassierer, w.Laden.Id));
        Assert.Equal(1, (await w.Besuche.GetFicheAsync(w.Besitzer, w.Kunde)).AnzahlBesuche);
    }

    [Fact]
    public async Task Fremde_Sehen_Nichts_Und_Notizen_Loescht_Nur_Verfasser_Oder_Besitzer()
    {
        var w = await BasisAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Besuche.GetAnwesendeAsync(w.Fremder, w.Laden.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Besuche.AnkunftAsync(w.Fremder, w.Laden.Id, w.Kunde, "Coupe", null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Besuche.GetFicheAsync(w.Fremder, w.Kunde));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Besuche.NotizAsync(w.Fremder, w.Kunde, "Hack"));

        await w.Besuche.NotizAsync(w.Besitzer, w.Kunde, "Note du propriétaire");
        w.Uhr.Jetzt = w.Uhr.Jetzt.AddMinutes(1);
        await w.Besuche.NotizAsync(w.Kassierer, w.Kunde, "Note de l'employée");
        var verlauf = (await w.Besuche.GetFicheAsync(w.Kassierer, w.Kunde)).Verlauf.Select(e => e.Notiz!).ToList();
        Assert.Equal([true, false], verlauf.Select(n => n.DarfLoeschen));                       // eigene ja, fremde nein

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Besuche.LoescheNotizAsync(w.Kassierer, verlauf[1].Id));
        await w.Besuche.LoescheNotizAsync(w.Kassierer, verlauf[0].Id);
        var rest = Assert.Single((await w.Besuche.GetFicheAsync(w.Besitzer, w.Kunde)).Verlauf);
        await w.Besuche.LoescheNotizAsync(w.Besitzer, rest.Notiz!.Id);
        Assert.Empty((await w.Besuche.GetFicheAsync(w.Besitzer, w.Kunde)).Verlauf);

        await Assert.ThrowsAsync<ArgumentException>(() => w.Besuche.NotizAsync(w.Besitzer, w.Kunde, "   "));
        await Assert.ThrowsAsync<ArgumentException>(() => w.Besuche.NotizAsync(w.Besitzer, w.Kunde, new string('x', KundenBesuchRegeln.NotizMax + 1)));
    }

    [Fact]
    public async Task Annulieren_Nur_Erfasser_Oder_Besitzer_Notizen_Bleiben()
    {
        var w = await BasisAsync();
        var besuchId = await w.Besuche.AnkunftAsync(w.Besitzer, w.Laden.Id, w.Kunde, "Coupe", null, "Vient pour la première fois");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Besuche.AnnuliereAsync(w.Kassierer, besuchId));
        await w.Besuche.AnnuliereAsync(w.Besitzer, besuchId);

        Assert.Empty(await w.Besuche.GetAnwesendeAsync(w.Besitzer, w.Laden.Id));
        var fiche = await w.Besuche.GetFicheAsync(w.Besitzer, w.Kunde);
        Assert.Equal(0, fiche.AnzahlBesuche);
        Assert.Equal("Vient pour la première fois", Assert.Single(fiche.Verlauf).Notiz!.Text);
    }

    [Fact]
    public async Task Encaissement_Mit_Kunde_Beendet_Den_Offenen_Besuch()
    {
        var w = await BasisAsync();
        Leistung coupe;
        await using (var db = await w.F.CreateDbContextAsync())
        {
            coupe = new Leistung { SalonId = w.Laden.Id, Name = "Coupe", Preis = 3000, IstAktiv = true };
            db.Leistungen.Add(coupe);
            await db.SaveChangesAsync();
        }
        var besuchId = await w.Besuche.AnkunftAsync(w.Kassierer, w.Laden.Id, w.Kunde, "Coupe", "Poste 1");
        var meldungen = 0;
        w.Signal.Geaendert += _ => meldungen++;

        var kasse = new KassenService(w.F, new FakeUmsatzBenachrichtigungService(), anwesenheit: w.Signal);
        var (ok, fehler, verkauf, _) = await kasse.BucheVerkaufAsync(w.Sitzung.Id,
            [new KassenService.VerkaufsPositionInput(coupe.Id, 1, null, false)],
            [new KassenService.ZahlungInput(ZahlungsArt.Bar, Gegeben: 3000)], kundeId: w.Kunde);
        Assert.True(ok, fehler);

        Assert.Empty(await w.Besuche.GetAnwesendeAsync(w.Kassierer, w.Laden.Id));
        await using var pruef = await w.F.CreateDbContextAsync();
        var besuch = await pruef.KundenBesuche.SingleAsync(b => b.Id == besuchId);
        Assert.NotNull(besuch.EndeUtc);
        Assert.Equal(verkauf!.Id, besuch.VerkaufId);
        Assert.Equal(1, meldungen);
        Assert.True((await w.Besuche.GetFicheAsync(w.Kassierer, w.Kunde)).Verlauf[0].Besuch!.MitVerkauf);
    }

    [Fact]
    public async Task Kunde_Loeschen_Entfernt_Passagen_Und_Notizen()
    {
        var w = await BasisAsync();
        var besuchId = await w.Besuche.AnkunftAsync(w.Kassierer, w.Laden.Id, w.Kunde, "Coupe", null, "Note de passage");
        await w.Besuche.NotizAsync(w.Besitzer, w.Kunde, "Note générale");
        await new KundenService(w.F, w.Uhr).LoescheAsync(w.Besitzer, w.Kunde);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.False(await db.KundenBesuche.AnyAsync());
        Assert.False(await db.KundenNotizen.AnyAsync());
        Assert.False(await db.KundenBesuche.AnyAsync(b => b.Id == besuchId));
    }

    [Theory]
    [InlineData("  coupe   femme ", "coupe femme")]
    [InlineData("ligne 1\r\n\r\n\r\nligne 2\n", "ligne 1\n\nligne 2")]
    [InlineData("\n\n  x  \n", "x")]
    public void Bereinige_Fasst_Leerraum_Zusammen(string eingabe, string erwartet)
        => Assert.Equal(erwartet, KundenBesuchRegeln.Bereinige(eingabe));

    [Fact]
    public void Besuch_Braucht_Anlass_Raum_Ist_Optional_Und_Dauer_Lesbar()
    {
        Assert.Throws<ArgumentException>(() => KundenBesuchRegeln.PruefeBesuch(" a ", null));
        Assert.Throws<ArgumentException>(() => KundenBesuchRegeln.PruefeBesuch("Coupe", new string('s', KundenBesuchRegeln.RaumMax + 1)));
        Assert.Equal(("Coupe", (string?)null), KundenBesuchRegeln.PruefeBesuch("Coupe", "  "));

        var t0 = new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);
        Assert.Equal("< 1 min", KundenBesuchRegeln.Dauer(t0, t0.AddSeconds(20)));
        Assert.Equal("35 min", KundenBesuchRegeln.Dauer(t0, t0.AddMinutes(35)));
        Assert.Equal("1 h 05", KundenBesuchRegeln.Dauer(t0, t0.AddMinutes(65)));
        Assert.Equal("à l'instant", KundenBesuchRegeln.Kurz(t0, t0.AddSeconds(20)));
        Assert.Equal("35 min", KundenBesuchRegeln.Kurz(t0, t0.AddMinutes(35)));
        Assert.Equal("à l'instant", KundenBesuchRegeln.Seit(t0, t0.AddSeconds(20)));
        Assert.Equal("depuis 1 h 05", KundenBesuchRegeln.Seit(t0, t0.AddMinutes(65)));
    }
}
