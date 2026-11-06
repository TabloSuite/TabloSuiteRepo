using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

public class PoAbrechnungTests
{
    [Fact]
    public async Task Verrechnung_Nur_Netto_Ueberweisen_Und_Keine_Doppelte_Provision()
    {
        var d = await BasisAsync();
        await d.Service.BestaetigeEingangAsync(d.Po, d.Abo, new DateTime(2026, 11, 5), "A");
        var k = Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20)));
        Assert.Equal(12500, k.VerrechnungVorschau);
        var ids = k.Eingaenge.Select(e => e.Id).ToArray();
        var lauf = Guid.NewGuid();
        var verrechnungen = new Dictionary<Guid, decimal> { [d.Po] = k.VerrechnungVorschau };
        await d.Service.FordereAsync(d.Admin, lauf, new DateTime(2026, 11, 20), new DateTime(2026, 11, 25), ids, verrechnungen: verrechnungen);
        await d.Service.FordereAsync(d.Admin, lauf, new DateTime(2026, 11, 20), new DateTime(2026, 11, 25), ids, verrechnungen: verrechnungen);
        k = Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20)));
        var a = Assert.Single(k.Anforderungen);
        Assert.Equal(150000, a.Betrag); Assert.Equal(12500, a.VerrechneteProvision); Assert.Equal(137500, a.NettoBetrag);
        Assert.Equal(137500, k.Abzufuehren); Assert.Equal(0, k.ProvisionOffen);
        Assert.Empty(k.Bewegungen);
        Assert.Single(k.Historie.Where(h => h.Aktion == "Commission conservée par compensation"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => d.Service.MeldeBewegungAsync(d.Admin, d.Po, null, true, 1, new DateTime(2026, 11, 20), "DOPPELT"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => d.Service.MeldeBewegungAsync(d.Po, d.Po, a.Id, false, 150000, new DateTime(2026, 11, 20), "BRUT"));
        await d.Service.MeldeBewegungAsync(d.Po, d.Po, a.Id, false, a.NettoBetrag, new DateTime(2026, 11, 20), "NET");
        k = Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20)));
        await d.Service.EntscheideAsync(d.Admin, Assert.Single(k.Bewegungen).Id, true, "Net reçu");
        k = Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20)));
        Assert.Equal(0, k.Abzufuehren); Assert.Equal(0, k.ProvisionOffen);
        Assert.Equal(137500, Assert.Single(k.Bewegungen).Betrag);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verrechnung_Schliesst_Reservierte_Oder_Ausgezahlte_Provision_Aus(bool bezahlt)
    {
        var d = await BasisAsync();
        await d.Service.BestaetigeEingangAsync(d.Po, d.Abo, new DateTime(2026, 11, 5), "A");
        var k = Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20)));
        var ids = k.Eingaenge.Select(e => e.Id).ToArray();
        await d.Service.MeldeBewegungAsync(d.Admin, d.Po, null, true, 5000, new DateTime(2026, 11, 20), "PROV");
        if (bezahlt)
        {
            var b = Assert.Single(Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20))).Bewegungen);
            await d.Service.EntscheideAsync(d.Po, b.Id, true, "Reçu");
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => d.Service.FordereAsync(d.Admin, Guid.NewGuid(), new DateTime(2026, 11, 20), new DateTime(2026, 11, 25), ids,
            verrechnungen: new Dictionary<Guid, decimal> { [d.Po] = 12500 }));
        k = Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20)));
        Assert.Empty(k.Anforderungen); Assert.Equal(7500, k.VerrechnungVorschau);
        await d.Service.FordereAsync(d.Admin, Guid.NewGuid(), new DateTime(2026, 11, 20), new DateTime(2026, 11, 25), ids,
            verrechnungen: new Dictionary<Guid, decimal> { [d.Po] = 7500 });
        k = Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20)));
        Assert.Equal(142500, Assert.Single(k.Anforderungen).NettoBetrag);
        Assert.Equal(bezahlt ? 0 : 5000, k.ProvisionOffen);
    }

    [Fact]
    public async Task Vollstaendige_Verrechnung_Erzeugt_Keinen_Fiktiven_Bankeingang()
    {
        var d = await BasisAsync();
        await using (var db = await d.Factory.CreateDbContextAsync())
        {
            var z = await db.ProduktOwnerSalons.SingleAsync(); z.ProzentProvision = 100; z.FestbetragProvision = 0;
            await db.SaveChangesAsync();
        }
        await d.Service.BestaetigeEingangAsync(d.Po, d.Abo, new DateTime(2026, 11, 5), "A");
        var k = Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20)));
        await d.Service.FordereAsync(d.Admin, Guid.NewGuid(), new DateTime(2026, 11, 20), new DateTime(2026, 11, 25), k.Eingaenge.Select(e => e.Id).ToArray(),
            verrechnungen: new Dictionary<Guid, decimal> { [d.Po] = 150000 });
        k = Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20)));
        Assert.Equal(0, Assert.Single(k.Anforderungen).NettoBetrag);
        Assert.Equal(0, k.Abzufuehren); Assert.Equal(0, k.ProvisionOffen); Assert.Empty(k.Bewegungen);
    }

    [Fact]
    public async Task Sammellauf_Erzeugt_Je_Po_Eigene_Feste_Anforderung_Und_Ueberspringt_Leere_Konten()
    {
        var d = await BasisAsync();
        await d.Service.BestaetigeEingangAsync(d.Po, d.Abo, new DateTime(2026, 11, 5), "A");
        await using (var db = await d.Factory.CreateDbContextAsync())
        {
            var salon = new Salon { Name = "Salon B" };
            var abo = new NutzungsAbo { Salon = salon, PreisSnapshot = 80000, ZahlungStatus = AboZahlungStatus.Bestaetigt };
            db.PoZahlungseingaenge.Add(new PoZahlungseingang { ProduktOwnerId = d.Fremd, NutzungsAbo = abo,
                Laden = "Salon B", Besitzer = "Autre propriétaire", Betrag = 80000, EingangUtc = new DateTime(2026, 11, 20, 23, 59, 59) });
            await db.SaveChangesAsync();
        }
        var konten = await d.Service.GetKontenAsync(d.Admin, new DateTime(2026, 11, 20));
        Assert.Equal(230000, konten.Sum(k => k.NeuAnzufordern));
        var ids = konten.SelectMany(k => k.Eingaenge).Select(e => e.Id).ToArray();
        var lauf = Guid.NewGuid();
        await d.Service.FordereAsync(d.Admin, lauf, new DateTime(2026, 11, 20), new DateTime(2026, 11, 25), ids);
        await using var kontrolle = await d.Factory.CreateDbContextAsync();
        var anforderungen = await kontrolle.PoZahlungsanforderungen.Include(a => a.Positionen).ToListAsync();
        Assert.Equal(2, anforderungen.Count);
        Assert.All(anforderungen, a => { Assert.Equal(lauf, a.SammellaufId); Assert.Equal(a.Betrag, a.Positionen.Sum(e => e.Betrag)); Assert.All(a.Positionen, e => Assert.Equal(a.ProduktOwnerId, e.ProduktOwnerId)); });
    }

    private class Uhr : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));
    }
    private sealed record Daten(IDbContextFactory<ApplicationDbContext> Factory, PoAbrechnungService Service,
        Guid Admin, Guid Po, Guid Fremd, Guid Abo, Guid Salon);
    private static async Task<Daten> BasisAsync()
    {
        var factory = TestDbContextFactory.Create();
        var admin = new AppUser { Id = Guid.NewGuid(), FullName = "Admin" };
        var po = new AppUser { Id = Guid.NewGuid(), FullName = "PO A" };
        var fremd = new AppUser { Id = Guid.NewGuid(), FullName = "PO B" };
        var owner = new AppUser { Id = Guid.NewGuid(), FullName = "Propriétaire" };
        var salon = new Salon { Name = "Boutique A" };
        var abo = new NutzungsAbo { Salon = salon, PreisSnapshot = 150000, ZeitraumVonUtc = new DateTime(2026, 11, 1),
            ZeitraumBisUtc = new DateTime(2026, 12, 1), ZahlungStatus = AboZahlungStatus.Gemeldet, ZahlungGemeldetUtc = new DateTime(2026, 11, 2) };
        await using var db = await factory.CreateDbContextAsync();
        foreach (var (u, role) in new[] { (admin, AppRollen.SuperAdmin), (po, AppRollen.ProduktOwner), (fremd, AppRollen.ProduktOwner), (owner, AppRollen.Besitzer) })
        {
            var r = await db.Roles.FirstOrDefaultAsync(r => r.Name == role) ?? new Role { Name = role };
            if (db.Entry(r).State == EntityState.Detached) db.Roles.Add(r);
            db.Users.Add(u); db.UserRoles.Add(new AppUserRole { UserId = u.Id, RoleId = r.Id });
            await db.SaveChangesAsync();
        }
        db.UserSalons.Add(new UserSalon { AppUserId = owner.Id, Salon = salon });
        db.ProduktOwnerSalons.Add(new ProduktOwnerSalon { ProduktOwnerId = po.Id, Salon = salon, ProzentProvision = 8, FestbetragProvision = 500, CreatedAtUtc = new DateTime(2026, 1, 1) });
        db.NutzungsAbos.Add(abo); await db.SaveChangesAsync();
        return new(factory, new PoAbrechnungService(factory, new Uhr()), admin.Id, po.Id, fremd.Id, abo.Id, salon.Id);
    }

    [Fact]
    public async Task Gesamter_Geldfluss_Mit_Teilzahlung_Separater_Provision_Und_Geteilter_Historie()
    {
        var d = await BasisAsync();
        await d.Service.BestaetigeEingangAsync(d.Po, d.Abo, new DateTime(2026, 11, 5), "RECU-1");
        await d.Service.BestaetigeEingangAsync(d.Po, d.Abo, new DateTime(2026, 11, 5), "RECU-1");
        var k = Assert.Single((await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20))));
        Assert.Equal(150000, k.NeuAnzufordern); Assert.Equal(12500, k.ProvisionOffen);
        var e = Assert.Single(k.Eingaenge);
        Assert.Equal("Propriétaire", e.Besitzer); Assert.Equal("Boutique A", e.Laden);
        var lauf = Guid.NewGuid();
        await d.Service.FordereAsync(d.Admin, lauf, new DateTime(2026, 11, 20), new DateTime(2026, 11, 25), [e.Id]);
        await d.Service.FordereAsync(d.Admin, lauf, new DateTime(2026, 11, 20), new DateTime(2026, 11, 25), [e.Id]);
        k = Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20)));
        var a = Assert.Single(k.Anforderungen); Assert.Equal(150000, a.Betrag); Assert.Single(a.Positionen); Assert.Equal(0, k.NeuAnzufordern);
        await d.Service.MeldeBewegungAsync(d.Po, d.Po, a.Id, false, 50000, new DateTime(2026, 11, 20), "VERSEMENT-1");
        await d.Service.MeldeBewegungAsync(d.Po, d.Po, a.Id, false, 50000, new DateTime(2026, 11, 20), "VERSEMENT-1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => d.Service.MeldeBewegungAsync(d.Po, d.Po, a.Id, false, 100001, new DateTime(2026, 11, 20), "TROP"));
        k = Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20)));
        Assert.Equal(150000, k.Abzufuehren);
        await d.Service.EntscheideAsync(d.Admin, Assert.Single(k.Bewegungen).Id, true, "Relevé bancaire vérifié");
        await d.Service.MeldeBewegungAsync(d.Admin, d.Po, null, true, 12500, new DateTime(2026, 11, 20), "COMMISSION-1");
        k = Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20)));
        Assert.Equal(100000, k.Abzufuehren); Assert.Equal(12500, k.ProvisionOffen);
        await d.Service.EntscheideAsync(d.Po, k.Bewegungen.Single(b => b.IstProvision).Id, true, "Reçu");
        k = Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20)));
        Assert.Equal(0, k.ProvisionOffen); Assert.Equal(100000, k.Abzufuehren);
        var adminKonto = (await d.Service.GetKontenAsync(d.Admin, new DateTime(2026, 11, 20))).Single(x => x.Id == d.Po);
        Assert.Equal(k.Historie.Count, adminKonto.Historie.Count); Assert.Equal(6, k.Historie.Count);
        Assert.Equal(0, (await new ProduktOwnerService(d.Factory).BerechneAuszahlungAsync(d.Po, 2026, 11)).Betrag);
    }

    [Fact]
    public async Task Stichtag_Rechte_Und_Stabile_Anforderungspositionen()
    {
        var d = await BasisAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => d.Service.BestaetigeEingangAsync(d.Fremd, d.Abo, new DateTime(2026, 11, 5), "X"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => d.Service.GetKontenAsync(Guid.NewGuid(), new DateTime(2026, 11, 20)));
        await d.Service.BestaetigeEingangAsync(d.Po, d.Abo, new DateTime(2026, 11, 5), "A");
        var k = Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 4)));
        Assert.Equal(0, k.NeuAnzufordern);
        var e = Assert.Single(k.Eingaenge);
        await Assert.ThrowsAsync<InvalidOperationException>(() => d.Service.FordereAsync(d.Admin, Guid.NewGuid(), new DateTime(2026, 11, 4), new DateTime(2026, 11, 25), [e.Id]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => d.Service.FordereAsync(d.Po, Guid.NewGuid(), new DateTime(2026, 11, 5), new DateTime(2026, 11, 25), [e.Id]));
        await d.Service.FordereAsync(d.Admin, Guid.NewGuid(), new DateTime(2026, 11, 5), new DateTime(2026, 11, 25), [e.Id]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => d.Service.FordereAsync(d.Admin, Guid.NewGuid(), new DateTime(2026, 11, 5), new DateTime(2026, 11, 25), [e.Id]));
        await using (var db = await d.Factory.CreateDbContextAsync())
        {
            (await db.Salons.SingleAsync(s => s.Id == d.Salon)).Name = "Nouveau nom";
            var z = await db.ProduktOwnerSalons.SingleAsync(z => z.SalonId == d.Salon); z.GueltigBisUtc = new DateTime(2026, 11, 10);
            db.ProduktOwnerSalons.Add(new ProduktOwnerSalon { SalonId = d.Salon, ProduktOwnerId = d.Fremd }); await db.SaveChangesAsync();
        }
        Assert.Empty(Assert.Single(await d.Service.GetKontenAsync(d.Fremd, new DateTime(2026, 11, 20))).Eingaenge);
        k = Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20)));
        Assert.Equal("Boutique A", Assert.Single(Assert.Single(k.Anforderungen).Positionen).Laden);
        var a = Assert.Single(k.Anforderungen);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => d.Service.MeldeBewegungAsync(d.Fremd, d.Fremd, a.Id, false, 1, new DateTime(2026, 11, 20), "X"));
    }

    [Fact]
    public async Task Ablehnung_Reserviert_Nicht_Dauerhaft_Und_Benoetigt_Begruendung()
    {
        var d = await BasisAsync();
        await d.Service.BestaetigeEingangAsync(d.Po, d.Abo, new DateTime(2026, 11, 5), "A");
        var e = Assert.Single(Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20))).Eingaenge);
        await d.Service.FordereAsync(d.Admin, Guid.NewGuid(), new DateTime(2026, 11, 20), new DateTime(2026, 11, 25), [e.Id]);
        var a = Assert.Single(Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20))).Anforderungen);
        await d.Service.MeldeBewegungAsync(d.Po, d.Po, a.Id, false, 150000, new DateTime(2026, 11, 20), "X");
        var b = Assert.Single(Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20))).Bewegungen);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => d.Service.EntscheideAsync(d.Po, b.Id, true, "X"));
        await Assert.ThrowsAsync<ArgumentException>(() => d.Service.EntscheideAsync(d.Admin, b.Id, false, ""));
        await d.Service.EntscheideAsync(d.Admin, b.Id, false, "Absent du relevé");
        await d.Service.MeldeBewegungAsync(d.Po, d.Po, a.Id, false, 150000, new DateTime(2026, 11, 20), "NOUVEAU");
        Assert.Equal(2, Assert.Single(await d.Service.GetKontenAsync(d.Po, new DateTime(2026, 11, 20))).Bewegungen.Count);
    }
}
