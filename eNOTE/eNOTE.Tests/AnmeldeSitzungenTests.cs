using ENote.Core.DataModels.Identity;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// 17.11.2026 « Appareils connectés »: Der Besitzer bleibt auf mehreren Geräten angemeldet (Kasse 1 + Kasse 2); andere Konten
/// behalten « eine Anmeldung ». Geräte lassen sich einzeln abmelden; Stempelwechsel (Passwort, Sperre) beendet alle.
/// </summary>
public class AnmeldeSitzungenTests
{
    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public DateTime Jetzt = jetzt;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    private static async Task<(Microsoft.EntityFrameworkCore.IDbContextFactory<ENote.WebApp.Data.ApplicationDbContext> F, Uhr Uhr,
        AnmeldeSitzungen S, Guid Besitzer, Guid Admin)> BasisAsync()
    {
        var f = TestDbContextFactory.Create();
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Ama" };
        var admin = new AppUser { Id = Guid.NewGuid(), FullName = "Admin" };
        await using (var db = await f.CreateDbContextAsync())
        {
            var rB = new Role { Name = AppRollen.Besitzer };
            var rA = new Role { Name = AppRollen.SuperAdmin };
            db.Roles.AddRange(rB, rA);
            db.Users.AddRange(besitzer, admin);
            db.UserRoles.AddRange(new AppUserRole { UserId = besitzer.Id, RoleId = rB.Id }, new AppUserRole { UserId = admin.Id, RoleId = rA.Id });
            await db.SaveChangesAsync();
        }
        var uhr = new Uhr(new DateTime(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc));
        return (f, uhr, new AnmeldeSitzungen(f, uhr), besitzer.Id, admin.Id);
    }

    private const string TabletUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 Edg/130.0";
    private const string HandyUa = "Mozilla/5.0 (Linux; Android 14; SM-A156B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36";

    [Fact]
    public async Task Besitzer_Bleibt_Auf_Beiden_Kassen_Angemeldet()
    {
        var (_, _, s, besitzer, _) = await BasisAsync();
        var tablet = await s.AnmeldenAsync(besitzer, TabletUa);
        var handy = await s.AnmeldenAsync(besitzer, HandyUa);

        Assert.Equal(tablet.Stempel, handy.Stempel);
        Assert.True((await s.PruefeAsync(besitzer, tablet.Stempel, tablet.SitzungId, TabletUa)).Gueltig);
        Assert.True((await s.PruefeAsync(besitzer, handy.Stempel, handy.SitzungId, HandyUa)).Gueltig);

        var liste = await s.GetGeraeteAsync(besitzer, handy.SitzungId);
        Assert.Equal(2, liste.Count);
        Assert.True(liste[0].IstDiesesGeraet);
        Assert.Equal("Chrome · Android", liste[0].Beschreibung);
        Assert.Equal("Edge · Windows", liste[1].Beschreibung);
    }

    [Fact]
    public async Task Andere_Konten_Behalten_Eine_Anmeldung()
    {
        var (_, _, s, _, admin) = await BasisAsync();
        var erste = await s.AnmeldenAsync(admin, TabletUa);
        var zweite = await s.AnmeldenAsync(admin, HandyUa);
        Assert.NotEqual(erste.Stempel, zweite.Stempel);
        // Altes Cookie trägt den alten Stempel – das Konto hat inzwischen den neuen.
        Assert.False((await s.PruefeAsync(admin, zweite.Stempel, erste.SitzungId, TabletUa)).Gueltig);
        Assert.True((await s.PruefeAsync(admin, zweite.Stempel, zweite.SitzungId, HandyUa)).Gueltig);
        Assert.Single(await s.GetGeraeteAsync(admin, zweite.SitzungId));
    }

    [Fact]
    public async Task Geraet_Einzeln_Oder_Alle_Anderen_Abmelden()
    {
        var (_, _, s, besitzer, _) = await BasisAsync();
        var a = await s.AnmeldenAsync(besitzer, TabletUa);
        var b = await s.AnmeldenAsync(besitzer, HandyUa);
        var c = await s.AnmeldenAsync(besitzer, HandyUa);

        Assert.True(await s.MeldeGeraetAbAsync(besitzer, b.SitzungId));
        Assert.False((await s.PruefeAsync(besitzer, a.Stempel, b.SitzungId, HandyUa)).Gueltig);
        Assert.True((await s.PruefeAsync(besitzer, a.Stempel, a.SitzungId, TabletUa)).Gueltig);

        Assert.Equal(1, await s.MeldeAndereAbAsync(besitzer, a.SitzungId));
        Assert.False((await s.PruefeAsync(besitzer, a.Stempel, c.SitzungId, HandyUa)).Gueltig);
        Assert.True((await s.PruefeAsync(besitzer, a.Stempel, a.SitzungId, TabletUa)).Gueltig);
        Assert.Single(await s.GetGeraeteAsync(besitzer, a.SitzungId));

        // Abmelden über « Se déconnecter » beendet nur dieses Gerät.
        await s.BeendeAsync(besitzer, a.SitzungId);
        Assert.Empty(await s.GetGeraeteAsync(besitzer, null));
    }

    [Fact]
    public async Task Stempelwechsel_Beendet_Alle_Geraete()
    {
        var (f, _, s, besitzer, _) = await BasisAsync();
        var a = await s.AnmeldenAsync(besitzer, TabletUa);
        var b = await s.AnmeldenAsync(besitzer, HandyUa);
        // Wie Passwortänderung, Kontosperre, Kündigung: ActiveSessionId wird neu gesetzt oder geleert.
        await using (var db = await f.CreateDbContextAsync())
        {
            (await db.Users.SingleAsync(u => u.Id == besitzer)).ActiveSessionId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }
        Guid neuerStempel;
        await using (var db = await f.CreateDbContextAsync())
            neuerStempel = (await db.Users.SingleAsync(u => u.Id == besitzer)).ActiveSessionId!.Value;
        Assert.False((await s.PruefeAsync(besitzer, neuerStempel, a.SitzungId, TabletUa)).Gueltig);
        Assert.False((await s.PruefeAsync(besitzer, neuerStempel, b.SitzungId, HandyUa)).Gueltig);
        Assert.Empty(await s.GetGeraeteAsync(besitzer, null));
    }

    [Fact]
    public async Task Altes_Cookie_Ohne_Geraet_Wird_Nachgetragen_Und_Aktivitaet_Gedrosselt()
    {
        var (f, uhr, s, besitzer, _) = await BasisAsync();
        var a = await s.AnmeldenAsync(besitzer, TabletUa);

        // Cookie von vor dem 17.11.2026: nur der Konto-Stempel, keine Geräte-Sitzung.
        var (gueltig, neu) = await s.PruefeAsync(besitzer, a.Stempel, null, HandyUa);
        Assert.True(gueltig);
        Assert.NotNull(neu);
        Assert.True((await s.PruefeAsync(besitzer, a.Stempel, neu, HandyUa)).Gueltig);
        Assert.Equal(2, (await s.GetGeraeteAsync(besitzer, a.SitzungId)).Count);

        // « Dernière activité » höchstens alle 5 Minuten geschrieben.
        uhr.Jetzt = uhr.Jetzt.AddMinutes(2);
        await s.PruefeAsync(besitzer, a.Stempel, a.SitzungId, TabletUa);
        await using (var db = await f.CreateDbContextAsync())
            Assert.Equal(new DateTime(2026, 11, 17, 8, 0, 0), (await db.AnmeldeSitzungen.SingleAsync(x => x.Id == a.SitzungId)).ZuletztUtc);
        uhr.Jetzt = uhr.Jetzt.AddMinutes(4);
        await s.PruefeAsync(besitzer, a.Stempel, a.SitzungId, TabletUa);
        await using (var db = await f.CreateDbContextAsync())
            Assert.Equal(uhr.Jetzt, (await db.AnmeldeSitzungen.SingleAsync(x => x.Id == a.SitzungId)).ZuletztUtc);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1", "Safari · iPhone")]
    [InlineData("Mozilla/5.0 (Linux; Android 13; SAMSUNG SM-T220) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/23.0 Chrome/115.0 Safari/537.36", "Samsung Internet · Android")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:131.0) Gecko/20100101 Firefox/131.0", "Firefox · Windows")]
    [InlineData(null, "Navigateur · appareil inconnu")]
    public void Geraetebeschreibung(string? ua, string erwartet) => Assert.Equal(erwartet, AnmeldeSitzungen.GeraetAus(ua));
}
