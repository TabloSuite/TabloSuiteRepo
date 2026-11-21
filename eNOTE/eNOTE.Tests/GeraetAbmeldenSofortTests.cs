using System.Security.Claims;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// 21.11.2026: « Déconnecter » unter « Appareils connectés » wirkt auch bei offener Seite – die Blazor-Verbindung prüft ihre
/// Anmeldung alle 30 s (<see cref="SitzungsRevalidierung"/>) und beim Seitenwechsel (<c>SitzungsWaechter</c>).
/// </summary>
public class GeraetAbmeldenSofortTests
{
    private const string Ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130.0 Safari/537.36";

    private static ClaimsPrincipal Benutzer(Guid userId, Guid stempel, Guid? geraet, params string[] rollen)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(AnmeldeSitzungen.KontoStempelClaim, stempel.ToString())
        };
        if (geraet is Guid g) claims.Add(new Claim(AnmeldeSitzungen.GeraetClaim, g.ToString()));
        claims.AddRange(rollen.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Identity.Application"));
    }

    private static async Task<(IDbContextFactory<ApplicationDbContext> F, AnmeldeSitzungen S, Guid Besitzer)> BasisAsync()
    {
        var f = TestDbContextFactory.Create();
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Ama" };
        await using (var db = await f.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.Besitzer };
            db.Roles.Add(rolle);
            db.Users.Add(besitzer);
            db.UserRoles.Add(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id });
            await db.SaveChangesAsync();
        }
        return (f, new AnmeldeSitzungen(f, TimeProvider.System), besitzer.Id);
    }

    [Fact]
    public async Task Abgemeldetes_Geraet_ist_ungueltig_das_andere_Geraet_nicht()
    {
        var (_, s, besitzer) = await BasisAsync();
        var kasse = await s.AnmeldenAsync(besitzer, Ua);
        var handy = await s.AnmeldenAsync(besitzer, Ua);
        var kassePrincipal = Benutzer(besitzer, kasse.Stempel, kasse.SitzungId, AppRollen.Besitzer);
        var handyPrincipal = Benutzer(besitzer, handy.Stempel, handy.SitzungId, AppRollen.Besitzer);
        Assert.True(await s.IstNochGueltigAsync(kassePrincipal));

        Assert.True(await s.MeldeGeraetAbAsync(besitzer, kasse.SitzungId));   // « Déconnecter » am Handy

        Assert.False(await s.IstNochGueltigAsync(kassePrincipal));
        Assert.True(await s.IstNochGueltigAsync(handyPrincipal));             // das Handy bleibt angemeldet
    }

    [Fact]
    public async Task Gesperrtes_Konto_oder_neuer_Stempel_ist_ungueltig()
    {
        var (f, s, besitzer) = await BasisAsync();
        var a = await s.AnmeldenAsync(besitzer, Ua);
        var principal = Benutzer(besitzer, a.Stempel, a.SitzungId, AppRollen.Besitzer);

        await using (var db = await f.CreateDbContextAsync())
        {
            (await db.Users.SingleAsync(u => u.Id == besitzer)).IsBlocked = true;
            await db.SaveChangesAsync();
        }
        Assert.False(await s.IstNochGueltigAsync(principal));

        await using (var db = await f.CreateDbContextAsync())
        {
            var u = await db.Users.SingleAsync(x => x.Id == besitzer);
            (u.IsBlocked, u.ActiveSessionId) = (false, Guid.NewGuid());        // z. B. Passwort geändert
            await db.SaveChangesAsync();
        }
        Assert.False(await s.IstNochGueltigAsync(principal));
        Assert.False(await s.IstNochGueltigAsync(Benutzer(besitzer, Guid.Empty, a.SitzungId)));
    }

    [Fact]
    public async Task Nicht_angemeldet_oder_altes_Cookie_ohne_Geraet_wird_nicht_abgemeldet()
    {
        var (_, s, besitzer) = await BasisAsync();
        var a = await s.AnmeldenAsync(besitzer, Ua);

        Assert.True(await s.IstNochGueltigAsync(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.True(await s.IstNochGueltigAsync(Benutzer(besitzer, a.Stempel, geraet: null, AppRollen.Besitzer)));
    }

    [Fact]
    public async Task Mitarbeiter_ohne_Connexion_personnelle_wird_abgemeldet()
    {
        var (f, s, _) = await BasisAsync();
        var awa = new AppUser { Id = Guid.NewGuid(), FullName = "Awa" };
        var salon = new Salon { Name = "Salon Test", IstAktiv = true, IstFreigegeben = true };
        await using (var db = await f.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.Kassierer };
            db.Roles.Add(rolle);
            db.Users.Add(awa);
            db.UserRoles.Add(new AppUserRole { UserId = awa.Id, RoleId = rolle.Id });
            db.Salons.Add(salon);
            db.UserSalons.Add(new UserSalon { AppUserId = awa.Id, SalonId = salon.Id, KannSichAnmelden = true });
            await db.SaveChangesAsync();
        }
        var a = await s.AnmeldenAsync(awa.Id, Ua);
        var principal = Benutzer(awa.Id, a.Stempel, a.SitzungId, AppRollen.Kassierer);
        Assert.True(await s.IstNochGueltigAsync(principal));

        await using (var db = await f.CreateDbContextAsync())
        {
            (await db.UserSalons.SingleAsync(us => us.AppUserId == awa.Id)).KannSichAnmelden = false;
            await db.SaveChangesAsync();
        }
        Assert.False(await s.IstNochGueltigAsync(principal));
    }

    private sealed class KaputteFactory : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => throw new InvalidOperationException("Datenbank nicht erreichbar");
    }

    [Fact]
    public async Task Pruefung_mit_Fehler_meldet_niemanden_ab()
    {
        var pruefung = new SitzungsRevalidierung(NullLoggerFactory.Instance, new AnmeldeSitzungen(new KaputteFactory(), TimeProvider.System));

        Assert.True(await pruefung.PruefeAsync(Benutzer(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AppRollen.Besitzer)));
        Assert.Equal(TimeSpan.FromSeconds(30), SitzungsRevalidierung.Intervall);
    }

    [Fact]
    public void Waechter_steckt_in_Routes_und_Anmeldeseite_kennt_den_Hinweis()
    {
        var komponenten = Path.Combine(RepoWurzel(), "eNOTE", "eNOTE.WebApp", "Components");
        Assert.Contains("<SitzungsWaechter />", File.ReadAllText(Path.Combine(komponenten, "Routes.razor")));
        Assert.Contains("SitzungsWaechter.HinweisCode", File.ReadAllText(Path.Combine(komponenten, "Pages", "Login.razor.cs")));
        Assert.Contains("AuthenticationStateProvider, SitzungsRevalidierung>",
            File.ReadAllText(Path.Combine(RepoWurzel(), "eNOTE", "eNOTE.WebApp", "Program.cs")));
    }

    private static string RepoWurzel([System.Runtime.CompilerServices.CallerFilePath] string quelle = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(quelle)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "eNOTE", "eNOTE.WebApp", "Program.cs")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo-Wurzel nicht gefunden.");
    }
}
