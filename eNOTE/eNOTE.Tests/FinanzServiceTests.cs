using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

public class FinanzServiceTests
{
    private static readonly DateTime Stichtag = new(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<(IDbContextFactory<ApplicationDbContext> Factory, Salon Salon, AppUser Benutzer)> BasisAsync(bool finanzrecht = true)
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var salon = new Salon { Name = "Salon A", IstFreigegeben = true };
        var benutzer = new AppUser { Id = Guid.NewGuid(), UserName = "proprietaire@example.test", FullName = "Propriétaire" };
        db.UserSalons.Add(new UserSalon { Salon = salon, AppUser = benutzer, KannFinanzenSehen = finanzrecht });
        await db.SaveChangesAsync();
        return (factory, salon, benutzer);
    }

    private static KassenSitzung Sitzung(Salon salon, AppUser benutzer, DateTime? geschlossen = null) => new()
    {
        SalonId = salon.Id,
        KassiererId = benutzer.Id,
        GestartetUtc = Stichtag.AddMonths(-2),
        AbgeschlossenUtc = geschlossen,
        Status = geschlossen is null ? KassenSitzungStatus.Offen : KassenSitzungStatus.Abgeschlossen
    };

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(500)]
    public async Task VerkaufsAnzahl_Liefert_Neueste_Eintraege_Des_Ausgewaehlten_Ladens(int anzahl)
    {
        var (factory, salon, benutzer) = await BasisAsync();
        await using var db = await factory.CreateDbContextAsync();
        var sitzung = Sitzung(salon, benutzer);
        db.KassenSitzungen.Add(sitzung);
        for (var i = 0; i < 510; i++)
        {
            db.Verkaeufe.Add(new Verkauf { SalonId = salon.Id, KassenSitzungId = sitzung.Id, ZeitUtc = Stichtag.AddMinutes(i), GesamtSumme = i });
        }
        var fremd = new Salon { Name = "Salon B", IstFreigegeben = true };
        var fremdeSitzung = Sitzung(fremd, benutzer);
        db.Salons.Add(fremd);
        db.KassenSitzungen.Add(fremdeSitzung);
        db.Verkaeufe.Add(new Verkauf { SalonId = fremd.Id, KassenSitzungId = fremdeSitzung.Id, ZeitUtc = Stichtag.AddDays(1), GesamtSumme = 9999 });
        await db.SaveChangesAsync();

        var daten = await new FinanzService(factory).GetVerkaeufeFuerFinanzenAsync(salon.Id, benutzer.Id, anzahl);

        Assert.Equal(anzahl, daten.Count);
        Assert.Equal(509m, daten[0].GesamtSumme);
        Assert.Equal(510m - anzahl, daten[^1].GesamtSumme);
        Assert.True(daten.Zip(daten.Skip(1)).All(p => p.First.ZeitUtc >= p.Second.ZeitUtc));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(501)]
    public async Task VerkaufsAnzahl_Lehnt_Ungueltige_Grenzen_Ab(int anzahl)
    {
        var (factory, salon, benutzer) = await BasisAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new FinanzService(factory)
            .GetVerkaeufeFuerFinanzenAsync(salon.Id, benutzer.Id, anzahl));
    }

    [Fact]
    public async Task Umsatz_Filtert_Vollstaendige_Tage_Und_Trennt_Zahlungsarten_Und_Laeden()
    {
        var (factory, salon, benutzer) = await BasisAsync();
        await using var db = await factory.CreateDbContextAsync();
        var sitzung = Sitzung(salon, benutzer);
        db.KassenSitzungen.Add(sitzung);
        db.Verkaeufe.AddRange(
            new Verkauf { SalonId = salon.Id, KassenSitzungId = sitzung.Id, ZeitUtc = Stichtag.AddTicks(-1), GesamtSumme = 1000 },
            new Verkauf { SalonId = salon.Id, KassenSitzungId = sitzung.Id, ZeitUtc = Stichtag, GesamtSumme = 100, ZahlungsArt = ZahlungsArt.Bar },
            new Verkauf { SalonId = salon.Id, KassenSitzungId = sitzung.Id, ZeitUtc = Stichtag.AddDays(1).AddTicks(-1), GesamtSumme = 200, ZahlungsArt = ZahlungsArt.Karte },
            new Verkauf { SalonId = salon.Id, KassenSitzungId = sitzung.Id, ZeitUtc = Stichtag.AddDays(1), GesamtSumme = 2000 });
        var fremd = new Salon { Name = "Salon B", IstFreigegeben = true };
        var fremdeSitzung = Sitzung(fremd, benutzer);
        db.Salons.Add(fremd);
        db.KassenSitzungen.Add(fremdeSitzung);
        db.Verkaeufe.Add(new Verkauf { SalonId = fremd.Id, KassenSitzungId = fremdeSitzung.Id, ZeitUtc = Stichtag, GesamtSumme = 9999 });
        await db.SaveChangesAsync();

        var tage = await new FinanzService(factory).GetTagesUmsaetzeImZeitraumAsync(salon.Id, benutzer.Id, Stichtag, Stichtag);

        var tag = Assert.Single(tage);
        Assert.Equal(2, tag.AnzahlVerkaeufe);
        Assert.Equal(100m, tag.Bar);
        Assert.Equal(200m, tag.Karte);
        Assert.Equal(300m, tag.Gesamt);
    }

    [Fact]
    public async Task Abschluesse_Filtern_Nach_Abschluss_Statt_Start_Und_Finden_Aeltere_Als_Zwanzig()
    {
        var (factory, salon, benutzer) = await BasisAsync();
        await using var db = await factory.CreateDbContextAsync();
        db.KassenSitzungen.AddRange(Enumerable.Range(0, 30).Select(i => Sitzung(salon, benutzer, Stichtag.AddDays(2).AddMinutes(i))));
        var morgens = Sitzung(salon, benutzer, Stichtag);
        var abends = Sitzung(salon, benutzer, Stichtag.AddDays(1).AddTicks(-1));
        db.KassenSitzungen.AddRange(morgens, abends,
            Sitzung(salon, benutzer, Stichtag.AddTicks(-1)),
            Sitzung(salon, benutzer, Stichtag.AddDays(1)), Sitzung(salon, benutzer));
        var fremd = new Salon { Name = "Salon B", IstFreigegeben = true };
        db.Salons.Add(fremd);
        db.KassenSitzungen.Add(Sitzung(fremd, benutzer, Stichtag));
        await db.SaveChangesAsync();

        var daten = await new FinanzService(factory).GetAbschluesseImZeitraumAsync(salon.Id, benutzer.Id, Stichtag, Stichtag);

        Assert.Equal(new[] { abends.Id, morgens.Id }, daten.Select(s => s.Id));
        Assert.All(daten, s => Assert.Equal(benutzer.FullName, s.Kassierer.FullName));
    }

    [Fact]
    public async Task Abschluesse_Seiten_Liefern_Zusatzzeile_Ohne_Duplikate_Bei_Gleicher_Uhrzeit()
    {
        var (factory, salon, benutzer) = await BasisAsync();
        await using var db = await factory.CreateDbContextAsync();
        db.KassenSitzungen.AddRange(Enumerable.Range(0, 110).Select(_ => Sitzung(salon, benutzer, Stichtag)));
        await db.SaveChangesAsync();
        var service = new FinanzService(factory);

        var erste = await service.GetAbschluesseImZeitraumAsync(salon.Id, benutzer.Id, Stichtag, Stichtag);
        var zweite = await service.GetAbschluesseImZeitraumAsync(salon.Id, benutzer.Id, Stichtag, Stichtag, 50);
        var dritte = await service.GetAbschluesseImZeitraumAsync(salon.Id, benutzer.Id, Stichtag, Stichtag, 100);

        Assert.Equal(51, erste.Count);
        Assert.Equal(51, zweite.Count);
        Assert.Equal(10, dritte.Count);
        Assert.Equal(erste[50].Id, zweite[0].Id);
        Assert.Equal(110, erste.Take(50).Concat(zweite.Take(50)).Concat(dritte).Select(s => s.Id).Distinct().Count());
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, true, true)]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    public async Task Finanzabfragen_Verweigern_Fehlende_Rechte_Oder_Gesperrten_Zugang(
        bool finanzrecht, bool gesperrt, bool aktiv, bool freigegeben)
    {
        var (factory, salon, benutzer) = await BasisAsync(finanzrecht);
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.SingleAsync();
        user.IsBlocked = gesperrt;
        var laden = await db.Salons.SingleAsync();
        laden.IstAktiv = aktiv;
        laden.IstFreigegeben = freigegeben;
        await db.SaveChangesAsync();
        var service = new FinanzService(factory);

        Assert.False(await service.DarfFinanzenSehenAsync(salon.Id, benutzer.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetVerkaeufeFuerFinanzenAsync(salon.Id, benutzer.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetTagesUmsaetzeImZeitraumAsync(salon.Id, benutzer.Id, Stichtag, Stichtag));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAbschluesseImZeitraumAsync(salon.Id, benutzer.Id, Stichtag, Stichtag));
    }

    [Fact]
    public async Task Besitzerrecht_Gilt_Nur_Mit_Zuordnung_Zum_Angefragten_Laden()
    {
        var (factory, salon, benutzer) = await BasisAsync(false);
        await using var db = await factory.CreateDbContextAsync();
        var rolle = new Role { Id = Guid.NewGuid(), Name = AppRollen.Besitzer, NormalizedName = AppRollen.Besitzer.ToUpperInvariant() };
        db.Roles.Add(rolle);
        db.UserRoles.Add(new AppUserRole { RoleId = rolle.Id, UserId = benutzer.Id });
        var fremd = new Salon { Name = "Salon B", IstFreigegeben = true };
        db.Salons.Add(fremd);
        await db.SaveChangesAsync();
        var service = new FinanzService(factory);

        Assert.True(await service.DarfFinanzenSehenAsync(salon.Id, benutzer.Id));
        Assert.False(await service.DarfFinanzenSehenAsync(fremd.Id, benutzer.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetTagesUmsaetzeImZeitraumAsync(fremd.Id, benutzer.Id, Stichtag, Stichtag));
    }

    [Fact]
    public async Task Finanzrecht_Wird_Fuer_Jeden_Laden_Und_Jede_Abfrage_Neu_Geprueft()
    {
        var (factory, salon, benutzer) = await BasisAsync();
        await using var db = await factory.CreateDbContextAsync();
        var zweiter = new Salon { Name = "Salon B", IstFreigegeben = true };
        db.UserSalons.Add(new UserSalon { Salon = zweiter, AppUserId = benutzer.Id, KannFinanzenSehen = false });
        await db.SaveChangesAsync();
        var service = new FinanzService(factory);
        Assert.True(await service.DarfFinanzenSehenAsync(salon.Id, benutzer.Id));
        Assert.False(await service.DarfFinanzenSehenAsync(zweiter.Id, benutzer.Id));

        (await db.UserSalons.SingleAsync(s => s.SalonId == salon.Id)).KannFinanzenSehen = false;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetVerkaeufeFuerFinanzenAsync(salon.Id, benutzer.Id));
    }

    [Fact]
    public async Task Datumsfilter_Lehnen_Umgekehrten_Zeitraum_Ab()
    {
        var (factory, salon, benutzer) = await BasisAsync();
        var service = new FinanzService(factory);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetTagesUmsaetzeImZeitraumAsync(salon.Id, benutzer.Id, Stichtag.AddDays(1), Stichtag));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAbschluesseImZeitraumAsync(salon.Id, benutzer.Id, Stichtag.AddDays(1), Stichtag));
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(0, 0)]
    [InlineData(0, 501)]
    public async Task Abschluesse_Lehnen_Ungueltige_Seitengrenzen_Ab(int ueberspringen, int anzahl)
    {
        var (factory, salon, benutzer) = await BasisAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new FinanzService(factory)
            .GetAbschluesseImZeitraumAsync(salon.Id, benutzer.Id, Stichtag, Stichtag, ueberspringen, anzahl));
    }

    [Fact]
    public async Task Finanzabfragen_Liefern_Leere_Listen_Ohne_Daten()
    {
        var (factory, salon, benutzer) = await BasisAsync();
        var service = new FinanzService(factory);
        Assert.Empty(await service.GetVerkaeufeFuerFinanzenAsync(salon.Id, benutzer.Id));
        Assert.Empty(await service.GetTagesUmsaetzeImZeitraumAsync(salon.Id, benutzer.Id, Stichtag, Stichtag));
        Assert.Empty(await service.GetAbschluesseImZeitraumAsync(salon.Id, benutzer.Id, Stichtag, Stichtag));
    }
}
