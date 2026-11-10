using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Regression: Besitzer sah andere Abo-Preise als der Admin (feste Plattformpreise statt
/// Monatsbasis × Monate − Rabatt; individueller Preis nur monatlich; Semestriel fehlte).
/// </summary>
public class AboPreisTests
{
    [Fact]
    public async Task Besitzer_Preise_Entsprechen_Admin_Tarif_Mit_Individuellem_Preis()
    {
        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Name = "Salon Bibi", IstAktiv = true, IstFreigegeben = true, IndividuellerMonatsPreis = 3500 };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            // Alte, widersprüchliche Festpreise dürfen keine Rolle mehr spielen.
            db.PlattformPreise.AddRange(
                new PlattformPreis { Intervall = AboIntervall.Monatlich, Preis = 5000, IstAktuell = true },
                new PlattformPreis { Intervall = AboIntervall.DreiMonate, Preis = 13500, IstAktuell = true },
                new PlattformPreis { Intervall = AboIntervall.Jaehrlich, Preis = 50000, IstAktuell = true });
            await db.SaveChangesAsync();
        }

        var formeln = await new AboService(factory).GetFormelPreiseAsync(salon.Id);

        // Werte exakt wie im Admin-Dialog (Screenshot): 3.500 · 9.975 · 19.425 · 37.800
        Assert.Equal([3500m, 9975m, 19425m, 37800m], formeln.Select(f => f.Preis));
        Assert.Equal(VertragsRegeln.Intervalle, formeln.Select(f => f.Intervall));
        Assert.All(formeln, f => Assert.True(f.Individuell));
        Assert.Equal(4200m, formeln.Single(f => f.Intervall == AboIntervall.Jaehrlich).Ersparnis);
    }

    [Fact]
    public async Task Globaler_Monatspreis_Aendern_Wirkt_Auch_Mit_Bestehender_Preis_Historie()
    {
        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Name = "Salon", IstAktiv = true, IstFreigegeben = true };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            db.MonatsPreisStaende.Add(new MonatsPreisStand { SalonId = null, MonatsPreis = 5000, GueltigAbUtc = DateTime.UtcNow.AddDays(-30) });
            db.PlattformPreise.Add(new PlattformPreis { Intervall = AboIntervall.Monatlich, Preis = 5000, IstAktuell = true });
            await db.SaveChangesAsync();
        }

        var admin = new AdminService(factory, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<AdminService>.Instance, null!);
        await admin.SetzePlattformPreisAsync(AboIntervall.Monatlich, 6000);

        var abo = new AboService(factory);
        Assert.Equal(6000m, await admin.GetGlobalenMonatsPreisAsync());
        Assert.Equal(VertragsRegeln.Preis(6000, AboIntervall.DreiMonate), await abo.GetPreisAsync(salon.Id, AboIntervall.DreiMonate));
    }

    [Fact]
    public async Task Automatische_Periode_Semestriel_Laeuft_Sechs_Monate_Mit_Preis_Snapshot()
    {
        var factory = TestDbContextFactory.Create();
        var start = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        var salon = new Salon
        {
            Name = "Salon", IstAktiv = true, IstFreigegeben = true, IndividuellerMonatsPreis = 4000,
            VertragsBeginnUtc = start, VertragsIntervall = AboIntervall.SechsMonate
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            await db.SaveChangesAsync();
        }

        await new VertragsService(factory, new FesteUhr(start.AddHours(1))).SynchronisiereAsync(salon.Id);

        await using var pruefung = await factory.CreateDbContextAsync();
        var abo = Assert.Single(pruefung.NutzungsAbos);
        Assert.Equal(start.AddMonths(6), abo.ZeitraumBisUtc);
        Assert.Equal(22200m, abo.PreisSnapshot); // 4.000 × 6 − 7,5 %
        Assert.Equal((4000m, 7.5m, true), (abo.MonatsBasisSnapshot!.Value, abo.RabattProzentSnapshot, abo.IndividuellerPreisSnapshot));
    }

    [Fact]
    public async Task Verspaetet_Erzeugte_Periode_Sperrt_Nicht_Ohne_Vorwarnung()
    {
        // Probezeit endete lange vor der Aktivierung der Automatik: die reguläre Frist
        // (15 Tage nach Fälligkeit) wäre schon vorbei – es gibt 15 Tage ab heute.
        var factory = TestDbContextFactory.Create();
        var probeEnde = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var heute = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
        var salon = new Salon { Name = "Salon", IstAktiv = true, IstFreigegeben = true, ProbeBisUtc = probeEnde, IndividuellerMonatsPreis = 3000 };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            await db.SaveChangesAsync();
        }

        var uhr = new FesteUhr(heute);
        var zugang = new KassenZugangService(factory, uhr);
        Assert.False((await zugang.PruefeAsync(salon.Id)).Gesperrt);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var perioden = db.NutzungsAbos.OrderBy(a => a.ZeitraumVonUtc).ToList();
            Assert.Equal(3, perioden.Count); // Sept., Okt. (rückwirkend), Nov.
            // Rückwirkende Perioden: frische Frist ab heute statt einer längst abgelaufenen.
            Assert.All(perioden.Take(2), a => Assert.Equal(heute.Date.AddDays(15), a.KassenSperreAbUtc));
            // Aktuelle Periode: reguläre Frist (15 Tage nach Fälligkeit) war noch nicht abgelaufen.
            Assert.Equal(new DateTime(2026, 11, 16), perioden[2].KassenSperreAbUtc);
        }

        uhr.Jetzt = new DateTime(2026, 11, 15, 23, 0, 0, DateTimeKind.Utc);
        Assert.False((await zugang.PruefeAsync(salon.Id)).Gesperrt);
        uhr.Jetzt = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
        Assert.True((await zugang.PruefeAsync(salon.Id)).Gesperrt);
    }

    [Fact]
    public async Task Periode_Vor_Dem_Ersten_Erfassten_Preis_Nutzt_Fruehesten_Globalen_Preis()
    {
        // Regression: Probezeit endete am 1.11., der Plattformpreis wurde erst am 10.11. erfasst und
        // ein anderer Laden hat bereits einen individuellen Preis-Stand → früher Abbruch mit
        // „Le tarif historique … doit être vérifié“, keine Rechnung, keine Sperre.
        var factory = TestDbContextFactory.Create();
        var probeEnde = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var erfasst = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
        var salon = new Salon { Name = "Demo", IstAktiv = true, IstFreigegeben = true, ProbeBisUtc = probeEnde };
        var anderer = new Salon { Name = "Bibi", IstAktiv = true, IstFreigegeben = true, IndividuellerMonatsPreis = 3500 };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.AddRange(salon, anderer);
            db.PlattformPreise.Add(new PlattformPreis { Intervall = AboIntervall.Monatlich, Preis = 5000, IstAktuell = true, GueltigAbUtc = erfasst });
            db.MonatsPreisStaende.Add(new MonatsPreisStand { SalonId = anderer.Id, MonatsPreis = 3500, GueltigAbUtc = erfasst });
            await db.SaveChangesAsync();
        }

        await new VertragsService(factory, new FesteUhr(erfasst.AddHours(1))).SynchronisiereAsync(salon.Id);

        await using var pruefung = await factory.CreateDbContextAsync();
        var abo = Assert.Single(pruefung.NutzungsAbos);
        Assert.Equal((probeEnde, 5000m, false), (abo.ZeitraumVonUtc, abo.PreisSnapshot, abo.IndividuellerPreisSnapshot));
    }

    private sealed class FesteUhr(DateTime jetzt) : TimeProvider
    {
        public DateTime Jetzt { get; set; } = jetzt;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Ohne_Konfigurierten_Preis_Keine_Formeln()
    {
        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Name = "Salon", IstAktiv = true, IstFreigegeben = true };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            await db.SaveChangesAsync();
        }

        var service = new AboService(factory);
        Assert.Empty(await service.GetFormelPreiseAsync(salon.Id));
        Assert.Equal(0m, await service.GetPreisAsync(salon.Id, AboIntervall.Monatlich));
    }
}
