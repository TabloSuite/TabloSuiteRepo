using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Nutzerwunsch 17.11.: Provision des Responsable entsteht automatisch mit der Bestätigung der Zahlung;
/// Zahlungen, Auszahlungen (Admin) und Empfangsbestätigungen (Responsable) lassen sich mehrfach auf einmal bestätigen.
/// </summary>
public class PoProvisionTests
{
    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public DateTime Jetzt = jetzt;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    private sealed record Daten(IDbContextFactory<ApplicationDbContext> Factory, Uhr Uhr, AboService Abos, PoProvisionService Provisionen,
        Guid Admin, Guid Po, Guid Po2, Guid Salon, Guid Salon2, Guid Abo1, Guid Abo2, Guid Abo3);

    private static async Task<Daten> BasisAsync()
    {
        var factory = TestDbContextFactory.Create();
        var uhr = new Uhr(new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc));
        var admin = new AppUser { Id = Guid.NewGuid(), FullName = "Admin" };
        var po = new AppUser { Id = Guid.NewGuid(), FullName = "Responsable A" };
        var po2 = new AppUser { Id = Guid.NewGuid(), FullName = "Responsable B" };
        var salon = new Salon { Name = "Boutique A" };
        var salon2 = new Salon { Name = "Salon B" };
        NutzungsAbo Abo(Salon s, decimal preis, int monat) => new()
        {
            Salon = s, PreisSnapshot = preis, ZeitraumVonUtc = new DateTime(2026, monat, 1), ZeitraumBisUtc = new DateTime(2026, monat + 1, 1),
            ZahlungStatus = AboZahlungStatus.Gemeldet, ZahlungGemeldetUtc = new DateTime(2026, 11, 2)
        };
        var abo1 = Abo(salon, 2500, 10);
        var abo2 = Abo(salon, 2500, 11);
        var abo3 = Abo(salon2, 4999, 11);
        await using var db = await factory.CreateDbContextAsync();
        foreach (var (u, rolle) in new[] { (admin, AppRollen.SuperAdmin), (po, AppRollen.ProduktOwner), (po2, AppRollen.ProduktOwner) })
        {
            var r = await db.Roles.FirstOrDefaultAsync(r => r.Name == rolle) ?? new Role { Name = rolle };
            if (db.Entry(r).State == EntityState.Detached) db.Roles.Add(r);
            db.Users.Add(u); db.UserRoles.Add(new AppUserRole { UserId = u.Id, RoleId = r.Id });
            await db.SaveChangesAsync();
        }
        db.ProduktOwnerSalons.Add(new ProduktOwnerSalon { ProduktOwnerId = po.Id, Salon = salon, ProzentProvision = 10, FestbetragProvision = 500, CreatedAtUtc = new DateTime(2026, 1, 1) });
        db.ProduktOwnerSalons.Add(new ProduktOwnerSalon { ProduktOwnerId = po2.Id, Salon = salon2, ProzentProvision = 7.5m, CreatedAtUtc = new DateTime(2026, 1, 1) });
        db.NutzungsAbos.AddRange(abo1, abo2, abo3);
        await db.SaveChangesAsync();
        return new(factory, uhr, new AboService(factory, null, uhr), new PoProvisionService(factory, uhr),
            admin.Id, po.Id, po2.Id, salon.Id, salon2.Id, abo1.Id, abo2.Id, abo3.Id);
    }

    [Fact]
    public async Task Bestaetigung_Legt_Provision_Automatisch_An_Fester_Anteil_Einmal_Je_Laden_Und_Monat()
    {
        var d = await BasisAsync();
        await d.Abos.SetzeZahlungsStatusAsync(d.Admin, d.Abo1, AboZahlungStatus.Bestaetigt);
        await d.Abos.SetzeZahlungsStatusAsync(d.Admin, d.Abo1, AboZahlungStatus.Bestaetigt); // doppelt: keine zweite Provision
        await d.Abos.SetzeZahlungsStatusAsync(d.Admin, d.Abo2, AboZahlungStatus.Bestaetigt);

        var u = await d.Provisionen.GetFuerPoAsync(d.Po, null);
        Assert.Equal(2, u.Eintraege.Count);
        var mitFest = Assert.Single(u.Eintraege, e => e.Fest > 0);
        Assert.Equal(750, mitFest.Betrag); // 2.500 × 10 % + 500
        var ohneFest = Assert.Single(u.Eintraege, e => e.Fest == 0);
        Assert.Equal(250, ohneFest.Betrag);
        Assert.All(u.Eintraege, e => Assert.Equal("Boutique A", e.Laden));
        Assert.Equal(1000, u.Summen.AVerser);

        // Responsable B betreut den Laden nicht → keine Provision; der Laden B rundet auf ganze Franc.
        Assert.Empty((await d.Provisionen.GetFuerPoAsync(d.Po2, null)).Eintraege);
        await d.Abos.SetzeZahlungsStatusAsync(d.Admin, d.Abo3, AboZahlungStatus.Bestaetigt);
        Assert.Equal(375, Assert.Single((await d.Provisionen.GetFuerPoAsync(d.Po2, null)).Eintraege).Betrag); // 4.999 × 7,5 % = 374,93
    }

    [Fact]
    public async Task Abgelehnte_Zahlung_Und_Laden_Ohne_Responsable_Ergeben_Keine_Provision()
    {
        var d = await BasisAsync();
        await d.Abos.SetzeZahlungsStatusAsync(d.Admin, d.Abo1, AboZahlungStatus.Abgelehnt);
        await using (var db = await d.Factory.CreateDbContextAsync())
        {
            (await db.ProduktOwnerSalons.SingleAsync(p => p.SalonId == d.Salon2)).GueltigBisUtc = new DateTime(2026, 11, 1);
            await db.SaveChangesAsync();
        }
        await d.Abos.SetzeZahlungsStatusAsync(d.Admin, d.Abo3, AboZahlungStatus.Bestaetigt);
        await using var pruefung = await d.Factory.CreateDbContextAsync();
        Assert.Empty(pruefung.PoProvisionen);
    }

    [Fact]
    public async Task Mehrere_Zahlungen_Auf_Einmal_Nur_Admin_Und_Nur_Gemeldete()
    {
        var d = await BasisAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => d.Abos.BestaetigeZahlungenAsync(d.Po, [d.Abo1, d.Abo2]));
        await d.Abos.SetzeZahlungsStatusAsync(d.Admin, d.Abo2, AboZahlungStatus.Abgelehnt);

        var (anzahl, summe) = await d.Abos.BestaetigeZahlungenAsync(d.Admin, [d.Abo1, d.Abo2, d.Abo3, d.Abo1]);
        Assert.Equal(2, anzahl); // Abo2 ist abgelehnt, Abo1 doppelt gewählt
        Assert.Equal(7499, summe);
        await using (var db = await d.Factory.CreateDbContextAsync())
        {
            Assert.Equal(AboZahlungStatus.Abgelehnt, (await db.NutzungsAbos.SingleAsync(a => a.Id == d.Abo2)).ZahlungStatus);
            Assert.Equal(2, await db.PoProvisionen.CountAsync());
        }
        Assert.Equal((0, 0m), await d.Abos.BestaetigeZahlungenAsync(d.Admin, [d.Abo1, d.Abo3]));
    }

    [Fact]
    public async Task Admin_Zahlt_Mehrere_Aus_Responsable_Bestaetigt_Mehrere_Oder_Meldet_Nicht_Erhalten()
    {
        var d = await BasisAsync();
        await d.Abos.BestaetigeZahlungenAsync(d.Admin, [d.Abo1, d.Abo2, d.Abo3]);
        var alle = (await d.Provisionen.GetFuerAdminAsync(d.Admin, null, PoProvisionStatus.AVerser)).Eintraege;
        Assert.Equal(3, alle.Count);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => d.Provisionen.MarkiereVerseeAsync(d.Po, alle.Select(e => e.Id).ToList(), d.Uhr.Jetzt, "X"));
        await Assert.ThrowsAsync<ArgumentException>(() => d.Provisionen.MarkiereVerseeAsync(d.Admin, alle.Select(e => e.Id).ToList(), d.Uhr.Jetzt, " "));
        await Assert.ThrowsAsync<ArgumentException>(() => d.Provisionen.MarkiereVerseeAsync(d.Admin, alle.Select(e => e.Id).ToList(), d.Uhr.Jetzt.AddDays(1), "X"));

        var (anzahl, summe) = await d.Provisionen.MarkiereVerseeAsync(d.Admin, alle.Select(e => e.Id).ToList(), d.Uhr.Jetzt, "MOMO-123");
        Assert.Equal(3, anzahl); Assert.Equal(1375, summe);
        await Assert.ThrowsAsync<InvalidOperationException>(() => d.Provisionen.MarkiereVerseeAsync(d.Admin, alle.Select(e => e.Id).ToList(), d.Uhr.Jetzt, "MOMO-124"));

        // Responsable A sieht nur seine zwei, kann die von B nicht bestätigen.
        var eigene = (await d.Provisionen.GetFuerPoAsync(d.Po, PoProvisionStatus.Versee)).Eintraege;
        Assert.Equal(2, eigene.Count);
        Assert.All(eigene, e => Assert.Equal("MOMO-123", e.Referenz));
        var vonB = alle.Single(e => e.PoId == d.Po2).Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => d.Provisionen.BestaetigeErhaltAsync(d.Po, [vonB]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => d.Provisionen.BestaetigeErhaltAsync(d.Admin, [vonB]));

        // Eine kam nicht an → zurück auf « à verser » mit Hinweis; die andere wird bestätigt.
        await Assert.ThrowsAsync<ArgumentException>(() => d.Provisionen.MeldeNichtErhaltenAsync(d.Po, eigene[0].Id, ""));
        await d.Provisionen.MeldeNichtErhaltenAsync(d.Po, eigene[0].Id, "Rien reçu sur Mobile Money");
        Assert.Equal((1, eigene[1].Betrag), await d.Provisionen.BestaetigeErhaltAsync(d.Po, [eigene[0].Id, eigene[1].Id]));

        var a = await d.Provisionen.GetFuerAdminAsync(d.Admin, d.Po, null);
        Assert.Equal(1, a.Summen.AnzahlAVerser); Assert.Equal(0, a.Summen.AnzahlVersee); Assert.Equal(1, a.Summen.AnzahlRecue);
        var zurueck = Assert.Single(a.Eintraege, e => e.Status == PoProvisionStatus.AVerser);
        Assert.Equal("Rien reçu sur Mobile Money", zurueck.Hinweis);
        Assert.Null(zurueck.Referenz);

        // Versement von B irrtümlich markiert → Admin nimmt es zurück; nach der Bestätigung geht das nicht mehr.
        await d.Provisionen.AnnuliereVersementAsync(d.Admin, vonB);
        Assert.Equal(PoProvisionStatus.AVerser, (await d.Provisionen.GetFuerPoAsync(d.Po2, null)).Eintraege.Single().Status);
        await d.Provisionen.MarkiereVerseeAsync(d.Admin, [vonB, zurueck.Id], d.Uhr.Jetzt, "MOMO-200");
        Assert.Null((await d.Provisionen.GetFuerAdminAsync(d.Admin, d.Po, null)).Eintraege.Single(e => e.Id == zurueck.Id).Hinweis);
        await d.Provisionen.BestaetigeErhaltAsync(d.Po2, [vonB]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => d.Provisionen.AnnuliereVersementAsync(d.Admin, vonB));

        await using var db = await d.Factory.CreateDbContextAsync();
        Assert.Contains(db.PoAbrechnungsEreignisse, h => h.Aktion == "Commission signalée non reçue" && h.ProduktOwnerId == d.Po);
        Assert.Equal(2, db.PoAbrechnungsEreignisse.Count(h => h.Aktion == "Commissions versées" && h.ProduktOwnerId == d.Po));
    }

    [Fact]
    public async Task Frueher_Bestaetigte_Zahlungen_Werden_Einmal_Ergaenzt_Ohne_Altes_Monatsverfahren_Doppelt_Zu_Zahlen()
    {
        var d = await BasisAsync();
        // Vor der Einführung bestätigt (ohne Provision): Abo1 im Oktober, Abo3 im November, Abo2 im November.
        await using (var db = await d.Factory.CreateDbContextAsync())
        {
            foreach (var (id, tag) in new[] { (d.Abo1, new DateTime(2026, 10, 5)), (d.Abo2, new DateTime(2026, 11, 3)), (d.Abo3, new DateTime(2026, 11, 4)) })
            {
                var abo = await db.NutzungsAbos.SingleAsync(a => a.Id == id);
                abo.ZahlungStatus = AboZahlungStatus.Bestaetigt; abo.ZahlungBestaetigtUtc = tag;
            }
            // Oktober von Responsable A wurde schon im alten Monatsverfahren berechnet (nach der Bestätigung).
            db.ProduktOwnerAuszahlungen.Add(new ProduktOwnerAuszahlung { ProduktOwnerId = d.Po, Jahr = 2026, Monat = 10, Betrag = 750, CreatedAtUtc = new DateTime(2026, 11, 1) });
            await db.SaveChangesAsync();
        }
        Assert.Equal(2, await d.Provisionen.ErgaenzeFehlendeAsync());
        Assert.Equal(0, await d.Provisionen.ErgaenzeFehlendeAsync());
        var a = Assert.Single((await d.Provisionen.GetFuerPoAsync(d.Po, null)).Eintraege);
        Assert.Equal(d.Abo2, (await HoleAsync(d, a.Id)).NutzungsAboId);
        Assert.Equal(750, a.Betrag); // erster im November → mit festem Anteil
        Assert.Single((await d.Provisionen.GetFuerPoAsync(d.Po2, null)).Eintraege);

        // Das alte Monatsverfahren zählt Zahlungen mit neuer Provision nicht noch einmal.
        Assert.Equal(0, (await new ProduktOwnerService(d.Factory).BerechneAuszahlungAsync(d.Po, 2026, 11)).Betrag);
    }

    private static async Task<PoProvision> HoleAsync(Daten d, Guid id)
    {
        await using var db = await d.Factory.CreateDbContextAsync();
        return await db.PoProvisionen.AsNoTracking().SingleAsync(p => p.Id == id);
    }
}
