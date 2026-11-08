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
    private static readonly DateTime Stichtag = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<(IDbContextFactory<ApplicationDbContext> Factory, Salon Salon, AppUser Benutzer)> BasisAsync()
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var salon = new Salon { Name = "Salon A", IstFreigegeben = true };
        var benutzer = new AppUser { Id = Guid.NewGuid(), UserName = "proprietaire@example.test", FullName = "Propriétaire" };
        db.UserSalons.Add(new UserSalon { Salon = salon, AppUser = benutzer, KannFinanzenSehen = true });
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

    [Fact]
    public async Task Letzte_Verkaeufe_Liefern_Neueste_Eintraege_Des_Ausgewaehlten_Ladens()
    {
        var (factory, salon, benutzer) = await BasisAsync();
        await using var db = await factory.CreateDbContextAsync();
        var sitzung = Sitzung(salon, benutzer);
        db.KassenSitzungen.Add(sitzung);
        for (var i = 0; i < 30; i++)
        {
            db.Verkaeufe.Add(new Verkauf { SalonId = salon.Id, KassenSitzungId = sitzung.Id, ZeitUtc = Stichtag.AddMinutes(i), GesamtSumme = i });
        }
        var fremd = new Salon { Name = "Salon B", IstFreigegeben = true };
        var fremdeSitzung = Sitzung(fremd, benutzer);
        db.Salons.Add(fremd);
        db.KassenSitzungen.Add(fremdeSitzung);
        db.Verkaeufe.Add(new Verkauf { SalonId = fremd.Id, KassenSitzungId = fremdeSitzung.Id, ZeitUtc = Stichtag.AddDays(1), GesamtSumme = 9999 });
        await db.SaveChangesAsync();

        var daten = await new FinanzService(factory).GetLetzteVerkaeufeAsync(salon.Id, 25);

        Assert.Equal(25, daten.Count);
        Assert.Equal(29m, daten[0].GesamtSumme);
        // Fremde Verkäufe werden nicht mitgeliefert (Mandantentrennung).
        Assert.DoesNotContain(daten, d => d.GesamtSumme == 9999m);
        Assert.True(daten.Zip(daten.Skip(1)).All(p => p.First.ZeitUtc >= p.Second.ZeitUtc));
    }

    [Fact]
    public async Task Umsatz_Gruppiert_Nach_Tag_Und_Trennt_Zahlungsarten_Und_Laeden()
    {
        var (factory, salon, benutzer) = await BasisAsync();
        await using var db = await factory.CreateDbContextAsync();
        var sitzung = Sitzung(salon, benutzer);
        db.KassenSitzungen.Add(sitzung);
        db.Verkaeufe.AddRange(
            new Verkauf { SalonId = salon.Id, KassenSitzungId = sitzung.Id, ZeitUtc = Stichtag, GesamtSumme = 100, ZahlungsArt = ZahlungsArt.Bar },
            new Verkauf { SalonId = salon.Id, KassenSitzungId = sitzung.Id, ZeitUtc = Stichtag.AddHours(1), GesamtSumme = 200, ZahlungsArt = ZahlungsArt.Karte });
        var fremd = new Salon { Name = "Salon B", IstFreigegeben = true };
        var fremdeSitzung = Sitzung(fremd, benutzer);
        db.Salons.Add(fremd);
        db.KassenSitzungen.Add(fremdeSitzung);
        db.Verkaeufe.Add(new Verkauf { SalonId = fremd.Id, KassenSitzungId = fremdeSitzung.Id, ZeitUtc = Stichtag, GesamtSumme = 9999 });
        await db.SaveChangesAsync();

        var tage = await new FinanzService(factory)
            .GetTagesUmsaetzeImZeitraumAsync(salon.Id, Stichtag.Date, Stichtag.Date.AddDays(1).AddTicks(-1));

        var tag = Assert.Single(tage);
        Assert.Equal(2, tag.AnzahlVerkaeufe);
        Assert.Equal(100m, tag.Bar);
        Assert.Equal(200m, tag.Karte);
        Assert.Equal(300m, tag.Gesamt);
    }

    [Fact]
    public async Task Abschluesse_Liefern_Nur_Abgeschlossene_Sitzungen_Des_Ladens()
    {
        var (factory, salon, benutzer) = await BasisAsync();
        await using var db = await factory.CreateDbContextAsync();
        var morgens = Sitzung(salon, benutzer, Stichtag);
        var abends = Sitzung(salon, benutzer, Stichtag.AddHours(5));
        db.KassenSitzungen.AddRange(morgens, abends, Sitzung(salon, benutzer));
        var fremd = new Salon { Name = "Salon B", IstFreigegeben = true };
        db.Salons.Add(fremd);
        db.KassenSitzungen.Add(Sitzung(fremd, benutzer, Stichtag));
        await db.SaveChangesAsync();

        var daten = await new FinanzService(factory).GetAbschluesseAsync(salon.Id, 20);

        Assert.Equal(new[] { abends.Id, morgens.Id }, daten.Select(s => s.Id));
        Assert.All(daten, s => Assert.Equal(KassenSitzungStatus.Abgeschlossen, s.Status));
    }

    [Fact]
    public async Task Finanzabfragen_Liefern_Leere_Listen_Ohne_Daten()
    {
        var (factory, salon, _) = await BasisAsync();
        var service = new FinanzService(factory);
        Assert.Empty(await service.GetLetzteVerkaeufeAsync(salon.Id));
        Assert.Empty(await service.GetTagesUmsaetzeImZeitraumAsync(salon.Id, Stichtag, Stichtag.AddDays(1)));
        Assert.Empty(await service.GetAbschluesseAsync(salon.Id));
    }
}
