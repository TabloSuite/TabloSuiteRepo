using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

public class VertragsKulanzTests
{
    private class Uhr(DateTime datum) : TimeProvider
    {
        public DateTime Jetzt = datum;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData(AboIntervall.Monatlich, 5000)]
    [InlineData(AboIntervall.DreiMonate, 14250)]
    [InlineData(AboIntervall.SechsMonate, 27750)]
    [InlineData(AboIntervall.Jaehrlich, 54000)]
    public void Preis_Rabatte(AboIntervall intervall, int erwartet)
        => Assert.Equal(erwartet, VertragsRegeln.Preis(5000, intervall));

    [Fact]
    public void Teilperiode_Schaltjahr_Und_Rundung()
    {
        var start = new DateTime(2028, 2, 15);
        Assert.Equal(new DateTime(2028, 3, 1), VertragsRegeln.PeriodenEnde(start, AboIntervall.Jaehrlich));
        Assert.Equal(1500, VertragsRegeln.Teilpreis(2900, start));
        Assert.Equal(29, VertragsRegeln.Preis(10, AboIntervall.DreiMonate));
    }

    [Theory]
    [InlineData(AboIntervall.Monatlich)]
    [InlineData(AboIntervall.DreiMonate)]
    [InlineData(AboIntervall.SechsMonate)]
    [InlineData(AboIntervall.Jaehrlich)]
    public async Task Kulanz_Bis_Tag_15_Nur_Bestaetigung_Entsperrt_Nicht_Blosses_Melden(AboIntervall intervall)
    {
        var factory = TestDbContextFactory.Create();
        var faellig = new DateTime(2026, 11, 1);
        var salon = new Salon { Name = "A", IstFreigegeben = true, VertragsBeginnUtc = faellig, VertragsIntervall = intervall, IndividuellerMonatsPreis = 1000 };
        await using (var db = await factory.CreateDbContextAsync()) { db.Salons.Add(salon); await db.SaveChangesAsync(); }
        var uhr = new Uhr(faellig.AddDays(14).AddHours(23));
        var service = new KassenZugangService(factory, uhr);
        Assert.False((await service.PruefeAsync(salon.Id)).Gesperrt);
        uhr.Jetzt = faellig.AddDays(15);
        Assert.True((await service.PruefeAsync(salon.Id)).Gesperrt);

        // Bloßes "als überwiesen markieren" (Gemeldet) entsperrt NICHT.
        await using (var db = await factory.CreateDbContextAsync())
        {
            var abo = Assert.Single(await db.NutzungsAbos.ToListAsync());
            Assert.Equal(faellig.AddDays(15), abo.KassenSperreAbUtc);
            abo.ZahlungStatus = AboZahlungStatus.Gemeldet;
            abo.ZahlungGemeldetUtc = uhr.Jetzt;
            await db.SaveChangesAsync();
        }
        Assert.True((await service.PruefeAsync(salon.Id)).Gesperrt);

        // Erst die Admin-Bestätigung entsperrt.
        await using (var db = await factory.CreateDbContextAsync())
        {
            var abo = Assert.Single(await db.NutzungsAbos.ToListAsync());
            abo.ZahlungStatus = AboZahlungStatus.Bestaetigt;
            abo.ZahlungBestaetigtUtc = uhr.Jetzt;
            await db.SaveChangesAsync();
        }
        Assert.False((await service.PruefeAsync(salon.Id)).Gesperrt);
    }

    [Fact]
    public async Task Gemeldete_Aber_Unbestaetigte_Zahlung_Sperrt_Nach_Frist_Weiter()
    {
        var factory = TestDbContextFactory.Create();
        var tag = new DateTime(2026, 11, 16);
        var a = new Salon { Name = "A", IstFreigegeben = true, VertragsBeginnUtc = tag, IndividuellerMonatsPreis = 1000 };
        var b = new Salon { Name = "B", IstFreigegeben = true, ProbeBisUtc = tag.AddDays(30) };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.AddRange(a, b);
            db.NutzungsAbos.AddRange(new NutzungsAbo { SalonId = a.Id, ZeitraumVonUtc = tag.AddMonths(-1), ZeitraumBisUtc = tag, ZahlungStatus = AboZahlungStatus.Offen },
                new NutzungsAbo { SalonId = a.Id, ZeitraumVonUtc = tag, ZeitraumBisUtc = tag.AddMonths(1), ZahlungStatus = AboZahlungStatus.Gemeldet });
            await db.SaveChangesAsync();
        }
        var service = new KassenZugangService(factory, new Uhr(tag));
        Assert.True((await service.PruefeAsync(a.Id)).Gesperrt);
        Assert.False((await service.PruefeAsync(b.Id)).Gesperrt);
    }
}
