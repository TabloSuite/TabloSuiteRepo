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

/// <summary>Tickets « en attente »: parken, für andere Kassierer sichtbar, fortsetzen (nur einmal), je Kunde ergänzen, Rechte, Katalogänderungen.</summary>
public class TicketParkTests
{
    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public DateTime Jetzt { get; set; } = jetzt;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    private sealed record Welt(IDbContextFactory<ApplicationDbContext> F, Salon Laden, Guid Besitzer, Guid Awa, Guid Kofi, Guid Fremder,
        Leistung Tresses, Leistung Brushing, Leistung Beurre, Leistung Divers, Leistung FremdeLeistung, Guid Kunde, Uhr Uhr,
        AnwesenheitsSignal Signal, TicketParkService Tickets);

    private static async Task<Welt> BasisAsync()
    {
        var f = TestDbContextFactory.Create();
        var laden = new Salon { Name = "Salon Élégance", IstAktiv = true, IstFreigegeben = true };
        var fremd = new Salon { Name = "Autre", IstAktiv = true, IstFreigegeben = true };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Ama Mensah" };
        var awa = new AppUser { Id = Guid.NewGuid(), FullName = "Awa Koffi" };
        var kofi = new AppUser { Id = Guid.NewGuid(), FullName = "Kofi Agbeko" };
        var fremder = new AppUser { Id = Guid.NewGuid(), FullName = "Fremd" };
        var tresses = new Leistung { SalonId = laden.Id, Name = "Tresses africaines", Preis = 15000m, IstAktiv = true };
        var brushing = new Leistung { SalonId = laden.Id, Name = "Brushing", Preis = 3000m, IstAktiv = true };
        var beurre = new Leistung { SalonId = laden.Id, Name = "Beurre de karité 500 ml", Preis = 4500m, IstAktiv = true, Typ = LeistungsTyp.Produit };
        var divers = new Leistung { SalonId = laden.Id, Name = "Divers", Preis = 0m, IstAktiv = true, IstStandardArtikel = true };
        var fremdeLeistung = new Leistung { SalonId = fremd.Id, Name = "Coupe", Preis = 1000m, IstAktiv = true };
        var kunde = new Kunde { SalonId = laden.Id, Name = "Akossiwa Mensah" };
        await using (var db = await f.CreateDbContextAsync())
        {
            var rB = new Role { Name = AppRollen.Besitzer };
            var rK = new Role { Name = AppRollen.Kassierer };
            db.Roles.AddRange(rB, rK);
            db.Users.AddRange(besitzer, awa, kofi, fremder);
            db.UserRoles.AddRange(new AppUserRole { UserId = besitzer.Id, RoleId = rB.Id }, new AppUserRole { UserId = awa.Id, RoleId = rK.Id },
                new AppUserRole { UserId = kofi.Id, RoleId = rK.Id }, new AppUserRole { UserId = fremder.Id, RoleId = rB.Id });
            db.Salons.AddRange(laden, fremd);
            db.UserSalons.AddRange(new UserSalon { AppUserId = besitzer.Id, SalonId = laden.Id }, new UserSalon { AppUserId = awa.Id, SalonId = laden.Id },
                new UserSalon { AppUserId = kofi.Id, SalonId = laden.Id }, new UserSalon { AppUserId = fremder.Id, SalonId = fremd.Id });
            db.Leistungen.AddRange(tresses, brushing, beurre, divers, fremdeLeistung);
            db.Kunden.Add(kunde);
            await db.SaveChangesAsync();
        }
        var uhr = new Uhr(new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc));
        var signal = new AnwesenheitsSignal();
        return new Welt(f, laden, besitzer.Id, awa.Id, kofi.Id, fremder.Id, tresses, brushing, beurre, divers, fremdeLeistung, kunde.Id,
            uhr, signal, new TicketParkService(f, uhr, signal));
    }

    [Fact]
    public async Task Geparkt_Sichtbar_Fuer_Andere_Kassierer_Und_Nur_Einmal_Fortsetzbar()
    {
        var w = await BasisAsync();
        var meldungen = 0;
        w.Signal.Geaendert += id => { if (id == w.Laden.Id) meldungen++; };

        var park = await w.Tickets.ParkeAsync(w.Awa, w.Laden.Id,
            [new(w.Tresses.Id, 1), new(w.Beurre.Id, 2), new(w.Divers.Id, 1, 750m)], null, "  dame en robe bleue,   revient dans 10 min ");
        Assert.False(park.Ergaenzt);

        // Schichtwechsel: Kofi meldet sich an und sieht das Ticket von Awa
        w.Uhr.Jetzt = w.Uhr.Jetzt.AddHours(3);
        var liste = await w.Tickets.GetAsync(w.Kofi, w.Laden.Id);
        var e = Assert.Single(liste);
        Assert.Equal("dame en robe bleue, revient dans 10 min", e.Notiz);
        Assert.Equal(4, e.Artikel);
        Assert.Equal(15000m + 2 * 4500m + 750m, e.Summe);
        Assert.Equal("Tresses africaines, 2× Beurre de karité 500 ml, Divers", e.Zeilen);
        Assert.Equal("Awa Koffi", e.ErstelltVon);
        Assert.Equal(1, await w.Tickets.ZaehleAsync(w.Besitzer, w.Laden.Id));

        // Preis geändert → beim Fortsetzen gilt der Katalog
        await using (var db = await w.F.CreateDbContextAsync())
        {
            (await db.Leistungen.SingleAsync(l => l.Id == w.Tresses.Id)).Preis = 16000m;
            await db.SaveChangesAsync();
        }
        Assert.Equal(16000m + 2 * 4500m + 750m, Assert.Single(await w.Tickets.GetAsync(w.Kofi, w.Laden.Id)).Summe);

        var u = await w.Tickets.UebernehmeAsync(w.Kofi, e.Id);
        Assert.Equal([(w.Tresses.Id, 1, (decimal?)null), (w.Beurre.Id, 2, null), (w.Divers.Id, 1, 750m)],
            u.Positionen.Select(p => (p.LeistungId, p.Menge, p.FreierBetrag)).ToList());
        Assert.Equal(0, u.Ausgelassen);
        Assert.Empty(await w.Tickets.GetAsync(w.Awa, w.Laden.Id));

        // Zweite Kasse versucht dasselbe Ticket
        var zweimal = await Assert.ThrowsAsync<ArgumentException>(() => w.Tickets.UebernehmeAsync(w.Awa, e.Id));
        Assert.Contains("n'est plus en attente", zweimal.Message);
        Assert.Equal(2, meldungen);
    }

    [Fact]
    public async Task Gleicher_Kunde_Ergaenzt_Sein_Wartendes_Ticket()
    {
        var w = await BasisAsync();
        var erst = await w.Tickets.ParkeAsync(w.Awa, w.Laden.Id, [new(w.Tresses.Id, 1)], w.Kunde, "Cabine 2");
        w.Uhr.Jetzt = w.Uhr.Jetzt.AddDays(1);   // Praxis: am nächsten Tag weitere Leistungen
        var zweit = await w.Tickets.ParkeAsync(w.Kofi, w.Laden.Id, [new(w.Tresses.Id, 1), new(w.Brushing.Id, 1)], w.Kunde, null);

        Assert.True(zweit.Ergaenzt);
        Assert.Equal(erst.TicketId, zweit.TicketId);
        var e = Assert.Single(await w.Tickets.GetAsync(w.Besitzer, w.Laden.Id));
        Assert.Equal(("Akossiwa Mensah", "Cabine 2", 3, "2× Tresses africaines, Brushing"), (e.KundeName, e.Notiz, e.Artikel, e.Zeilen));
        Assert.Equal("Awa Koffi", e.ErstelltVon);
        Assert.Equal("Kofi Agbeko", e.GeaendertVon);

        var u = await w.Tickets.UebernehmeFuerKundeAsync(w.Awa, w.Laden.Id, w.Kunde);
        Assert.NotNull(u);
        Assert.Equal(w.Kunde, u!.KundeId);
        Assert.Null(await w.Tickets.UebernehmeFuerKundeAsync(w.Awa, w.Laden.Id, w.Kunde));
    }

    [Fact]
    public async Task Fremde_Und_Ungueltige_Eingaben_Werden_Abgewiesen()
    {
        var w = await BasisAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Tickets.ParkeAsync(w.Fremder, w.Laden.Id, [new(w.Tresses.Id, 1)], null, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Tickets.GetAsync(w.Fremder, w.Laden.Id));
        Assert.Equal(0, await w.Tickets.ZaehleAsync(w.Fremder, w.Laden.Id));

        await Assert.ThrowsAsync<ArgumentException>(() => w.Tickets.ParkeAsync(w.Awa, w.Laden.Id, [], null, null));
        await Assert.ThrowsAsync<ArgumentException>(() => w.Tickets.ParkeAsync(w.Awa, w.Laden.Id, [new(w.FremdeLeistung.Id, 1)], null, null));
        await Assert.ThrowsAsync<ArgumentException>(() => w.Tickets.ParkeAsync(w.Awa, w.Laden.Id, [new(w.Tresses.Id, 0)], null, null));
        await Assert.ThrowsAsync<ArgumentException>(() => w.Tickets.ParkeAsync(w.Awa, w.Laden.Id, [new(w.Tresses.Id, 1, 500m)], null, null));
        await Assert.ThrowsAsync<ArgumentException>(() => w.Tickets.ParkeAsync(w.Awa, w.Laden.Id, [new(w.Tresses.Id, 1)], null, new string('x', 121)));

        var t = await w.Tickets.ParkeAsync(w.Awa, w.Laden.Id, [new(w.Tresses.Id, 1)], null, null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Tickets.UebernehmeAsync(w.Fremder, t.TicketId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Tickets.LoescheAsync(w.Fremder, t.TicketId));
        await w.Tickets.LoescheAsync(w.Kofi, t.TicketId);
        Assert.Empty(await w.Tickets.GetAsync(w.Awa, w.Laden.Id));
    }

    [Fact]
    public async Task Inaktive_Leistung_Wird_Beim_Fortsetzen_Ausgelassen_Und_Geloeschte_Verschwindet()
    {
        var w = await BasisAsync();
        var a = await w.Tickets.ParkeAsync(w.Awa, w.Laden.Id, [new(w.Tresses.Id, 1), new(w.Brushing.Id, 1)], null, "A");
        var b = await w.Tickets.ParkeAsync(w.Awa, w.Laden.Id, [new(w.Brushing.Id, 2)], null, "B");

        await using (var db = await w.F.CreateDbContextAsync())
        {
            (await db.Leistungen.SingleAsync(l => l.Id == w.Tresses.Id)).IstAktiv = false;
            await db.SaveChangesAsync();
        }
        // Brushing wird ganz gelöscht: Ticket B (nur Brushing) verschwindet, Ticket A behält Tresses
        await new KatalogService(w.F, null!, null!).LoescheLeistungAsync(w.Brushing.Id);

        var e = Assert.Single(await w.Tickets.GetAsync(w.Awa, w.Laden.Id));
        Assert.Equal((a.TicketId, "Tresses africaines"), (e.Id, e.Zeilen));
        var u = await w.Tickets.UebernehmeAsync(w.Awa, a.TicketId);
        Assert.Empty(u.Positionen);
        Assert.Equal(1, u.Ausgelassen);
        _ = b;
    }

    /// <summary>16.11.2026: Eine schon verkaufte Leistung scheiterte beim Löschen an der Datenbank (Restrict, unbehandelt) –
    /// jetzt wird sie wie eine benutzte Remise nur deaktiviert; eine nie benutzte wird weiter gelöscht.</summary>
    [Fact]
    public async Task Verkaufte_Leistung_Wird_Beim_Loeschen_Nur_Deaktiviert()
    {
        var w = await BasisAsync();
        await using (var db = await w.F.CreateDbContextAsync())
        {
            var v = new Verkauf { KassenSitzungId = Guid.NewGuid(), SalonId = w.Laden.Id, GesamtSumme = 15000m, ZeitUtc = w.Uhr.Jetzt };
            v.Positionen.Add(new VerkaufsPosition { LeistungId = w.Tresses.Id, LeistungName = w.Tresses.Name, Menge = 1, ZeilenSumme = 15000m });
            db.Verkaeufe.Add(v);
            await db.SaveChangesAsync();
        }
        var katalog = new KatalogService(w.F, null!, null!);

        Assert.False(await katalog.LoescheLeistungAsync(w.Tresses.Id));   // verkauft → nur deaktiviert
        Assert.True(await katalog.LoescheLeistungAsync(w.Brushing.Id));   // nie benutzt → gelöscht

        await using var db2 = await w.F.CreateDbContextAsync();
        Assert.False((await db2.Leistungen.SingleAsync(l => l.Id == w.Tresses.Id)).IstAktiv);
        Assert.False(await db2.Leistungen.AnyAsync(l => l.Id == w.Brushing.Id));
    }

    [Theory]
    [InlineData("Tresses africaines", new[] { "Tresses africaines" })]
    [InlineData("  tresses   AFRICAINES ", new[] { "Tresses africaines" })]
    [InlineData("Defrisage", new[] { "Défrisage" })]
    [InlineData("Coupe + barbe", new[] { "Coupe + barbe" })]
    [InlineData("Brushing, Manucure", new[] { "Brushing", "Manucure" })]
    [InlineData("Brushing et soin", new[] { "Brushing" })]
    [InlineData("Consultation", new string[0])]
    [InlineData("", new string[0])]
    public void Anlass_Findet_Passende_Leistungen(string anlass, string[] erwartet)
    {
        string[] katalog = ["Tresses africaines", "Défrisage", "Coupe + barbe", "Coupe homme", "Brushing", "Manucure"];
        Assert.Equal(erwartet, TicketParkRegeln.PassendZumAnlass(anlass, katalog, n => n));
    }
}
