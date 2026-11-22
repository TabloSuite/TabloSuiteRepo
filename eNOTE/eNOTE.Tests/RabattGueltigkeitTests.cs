using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Fehlersuche 21.11.2026: Remise « Du = Au = heute » wurde mit 00:00 gespeichert und mit der Uhrzeit verglichen – am
/// letzten Tag galt sie ab 00:01 nicht mehr. Die Kasse bot sie trotzdem an, der Verkauf scheiterte mit « La remise
/// choisie … n'est pas valide ». Jetzt Kalendertage einschließlich, gleiche Regel für Kasse, Server und Seite.
/// </summary>
public class RabattGueltigkeitTests
{
    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(jetzt, DateTimeKind.Utc));
    }

    private static readonly DateTime Tag = new(2026, 11, 23);

    [Theory]
    [InlineData(0, 1, true)]      // 00:01 am Tag
    [InlineData(0, 21 * 60 + 30, true)]   // 21:30 am letzten Tag
    [InlineData(0, 23 * 60 + 59, true)]   // 23:59 am letzten Tag
    [InlineData(1, 1, false)]     // nächster Tag
    [InlineData(-1, 23 * 60, false)]      // Vortag
    public void Du_Und_Au_Gelten_Den_Ganzen_Tag(int tagVersatz, int minuten, bool erwartet)
    {
        var r = new Rabatt { GueltigVonUtc = Tag, GueltigBisUtc = Tag };
        Assert.Equal(erwartet, KatalogService.IstRabattGueltig(r, Tag.AddDays(tagVersatz).AddMinutes(minuten)));
    }

    [Fact]
    public void Au_Vor_Du_Wird_Abgelehnt()
    {
        var r = new Rabatt { Name = "Inversé", Art = RabattArt.Prozent, Wert = 5, GueltigVonUtc = Tag, GueltigBisUtc = Tag.AddDays(-1) };
        Assert.Contains("« Au »", KatalogService.PruefeRabatt(r));
    }

    [Fact]
    public async Task Kasse_Bucht_Remise_Am_Letzten_Gueltigkeitstag_Um_21_Uhr()
    {
        var f = TestDbContextFactory.Create();
        var salon = new Salon { Name = "Remise Tag", IstAktiv = true, IstFreigegeben = true };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Besitzer" };
        var tresses = new Leistung { SalonId = salon.Id, Name = "Tresses", Preis = 12000m, IstAktiv = true };
        var rabatt = new Rabatt { SalonId = salon.Id, Name = "Jour J", Art = RabattArt.Prozent, Wert = 5, IstAktiv = true, GueltigVonUtc = Tag, GueltigBisUtc = Tag };
        await using (var db = await f.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.Besitzer };
            db.Roles.Add(rolle); db.Users.Add(besitzer); db.UserRoles.Add(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id });
            db.Salons.Add(salon); db.UserSalons.Add(new UserSalon { AppUserId = besitzer.Id, SalonId = salon.Id });
            db.Leistungen.Add(tresses); db.Rabatte.Add(rabatt);
            db.KassenSitzungen.Add(new KassenSitzung { SalonId = salon.Id, KassiererId = besitzer.Id, Status = KassenSitzungStatus.Offen });
            await db.SaveChangesAsync();
        }
        Guid sitzungId;
        await using (var db = await f.CreateDbContextAsync()) sitzungId = db.KassenSitzungen.Single().Id;
        var kasse = new KassenService(f, new FakeUmsatzBenachrichtigungService(), new Uhr(Tag.AddHours(21)), new AnwesenheitsSignal());
        var (ok, fehler, verkauf, _) = await kasse.BucheVerkaufAsync(sitzungId,
            [new KassenService.VerkaufsPositionInput(tresses.Id, 1, rabatt.Id, false)], [new KassenService.ZahlungInput(ZahlungsArt.Karte)]);
        Assert.True(ok, fehler);
        Assert.Equal(600m, verkauf!.RabattSumme);
    }
}
