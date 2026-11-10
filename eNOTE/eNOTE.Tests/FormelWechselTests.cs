using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

/// <summary>Formelwechsel bei noch offener Rechnung: sofortige Umwandlung, sonst ab nächster Periode.</summary>
public class FormelWechselTests
{
    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public DateTime Jetzt { get; set; } = jetzt;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    private static readonly DateTime November = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<(TestFactory F, Salon Salon, AppUser Besitzer)> BasisAsync(DateTime vertragsBeginn)
    {
        var factory = TestDbContextFactory.Create();
        var salon = new Salon
        {
            Name = "Salon", IstAktiv = true, IstFreigegeben = true, IndividuellerMonatsPreis = 4000,
            VertragsBeginnUtc = vertragsBeginn, VertragsIntervall = AboIntervall.Monatlich
        };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Ama" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.Besitzer };
            db.Roles.Add(rolle);
            db.UserRoles.Add(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id });
            db.UserSalons.Add(new UserSalon { AppUser = besitzer, Salon = salon });
            await db.SaveChangesAsync();
        }
        return (new TestFactory(factory), salon, besitzer);
    }

    private sealed record TestFactory(Microsoft.EntityFrameworkCore.IDbContextFactory<ENote.WebApp.Data.ApplicationDbContext> Inner);

    [Fact]
    public async Task Offene_Monatsrechnung_Wird_Sofort_In_Trimestriel_Umgewandelt_Frist_Bleibt()
    {
        var (f, salon, besitzer) = await BasisAsync(November);
        var uhr = new Uhr(November.AddDays(5));
        var service = new VertragsService(f.Inner, uhr);
        await service.SynchronisiereAsync(salon.Id);

        // Admin erhöht zwischendurch den Preis – die Umwandlung nutzt den Preis vom Periodenbeginn.
        await using (var db = await f.Inner.CreateDbContextAsync())
        {
            var s = await db.Salons.SingleAsync();
            s.IndividuellerMonatsPreis = 6000;
            db.MonatsPreisStaende.Add(new MonatsPreisStand { SalonId = salon.Id, MonatsPreis = 6000, GueltigAbUtc = uhr.Jetzt });
            await db.SaveChangesAsync();
        }

        var vorschau = await service.GetWechselVorschauAsync(besitzer.Id, salon.Id, AboIntervall.DreiMonate);
        Assert.Equal((VertragsService.WechselArt.SofortUmgewandelt, 11400m), (vorschau.Art, vorschau.NeuerBetrag!.Value)); // 4.000 × 3 − 5 %

        Assert.Equal(VertragsService.WechselArt.SofortUmgewandelt, await service.WechsleAsync(besitzer.Id, salon.Id, AboIntervall.DreiMonate));

        await using (var db = await f.Inner.CreateDbContextAsync())
        {
            var abo = await db.NutzungsAbos.SingleAsync();
            Assert.Equal((AboIntervall.DreiMonate, November, November.AddMonths(3), 11400m, 5m),
                (abo.Intervall, abo.ZeitraumVonUtc, abo.ZeitraumBisUtc, abo.PreisSnapshot, abo.RabattProzentSnapshot));
            Assert.Equal(November.AddDays(15), abo.KassenSperreAbUtc); // Frist unverändert
            Assert.Equal("Formule convertie", (await db.VertragsHistorien.SingleAsync()).Aktion);
            var s = await db.Salons.SingleAsync();
            Assert.Equal((AboIntervall.DreiMonate, (AboIntervall?)null), (s.VertragsIntervall, s.NaechstesVertragsIntervall));
        }

        // Nächste Periode beginnt erst nach den 3 Monaten – mit dem dann gültigen Preis.
        uhr.Jetzt = November.AddMonths(3);
        await service.SynchronisiereAsync(salon.Id);
        await using (var db = await f.Inner.CreateDbContextAsync())
        {
            var naechste = await db.NutzungsAbos.OrderBy(a => a.ZeitraumVonUtc).LastAsync();
            Assert.Equal((November.AddMonths(3), AboIntervall.DreiMonate, 17100m), (naechste.ZeitraumVonUtc, naechste.Intervall, naechste.PreisSnapshot));
        }
    }

    [Fact]
    public async Task Gemeldete_Oder_Anteilige_Rechnung_Wird_Nicht_Umgewandelt()
    {
        // Anteilige Startrechnung (Vertrag beginnt am 10.)
        var (f, salon, besitzer) = await BasisAsync(November.AddDays(9));
        var uhr = new Uhr(November.AddDays(12));
        var service = new VertragsService(f.Inner, uhr);
        await service.SynchronisiereAsync(salon.Id);
        Assert.Equal(VertragsService.WechselArt.AbNaechsterPeriode, await service.WechsleAsync(besitzer.Id, salon.Id, AboIntervall.Jaehrlich));
        await using (var db = await f.Inner.CreateDbContextAsync())
        {
            var abo = await db.NutzungsAbos.SingleAsync();
            Assert.True(abo.IstTeilperiode);
            Assert.Equal(AboIntervall.Monatlich, abo.Intervall);
            Assert.Equal(AboIntervall.Jaehrlich, (await db.Salons.SingleAsync()).NaechstesVertragsIntervall);
        }

        // Volle, aber bereits gemeldete Rechnung
        var (f2, salon2, besitzer2) = await BasisAsync(November);
        var service2 = new VertragsService(f2.Inner, new Uhr(November.AddDays(3)));
        await service2.SynchronisiereAsync(salon2.Id);
        Guid aboId;
        await using (var db = await f2.Inner.CreateDbContextAsync()) aboId = (await db.NutzungsAbos.SingleAsync()).Id;
        await service2.MeldeAsync(besitzer2.Id, aboId, null);
        Assert.Equal(VertragsService.WechselArt.AbNaechsterPeriode, await service2.WechsleAsync(besitzer2.Id, salon2.Id, AboIntervall.DreiMonate));
        await using (var db = await f2.Inner.CreateDbContextAsync())
        {
            Assert.Equal((AboIntervall.Monatlich, 4000m), ((await db.NutzungsAbos.SingleAsync()).Intervall, (await db.NutzungsAbos.SingleAsync()).PreisSnapshot));
        }
    }

    [Fact]
    public async Task Abgelehnte_Zahlung_Erlaubt_Umwandlung_Und_Fremde_Duerfen_Nicht()
    {
        var (f, salon, besitzer) = await BasisAsync(November);
        var service = new VertragsService(f.Inner, new Uhr(November.AddDays(2)));
        await service.SynchronisiereAsync(salon.Id);
        await using (var db = await f.Inner.CreateDbContextAsync())
        {
            (await db.NutzungsAbos.SingleAsync()).ZahlungStatus = AboZahlungStatus.Abgelehnt;
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.WechsleAsync(Guid.NewGuid(), salon.Id, AboIntervall.SechsMonate));
        Assert.Equal(VertragsService.WechselArt.SofortUmgewandelt, await service.WechsleAsync(besitzer.Id, salon.Id, AboIntervall.SechsMonate));
        await using (var db = await f.Inner.CreateDbContextAsync())
        {
            Assert.Equal(22200m, (await db.NutzungsAbos.SingleAsync()).PreisSnapshot); // 4.000 × 6 − 7,5 %
        }
    }
}
