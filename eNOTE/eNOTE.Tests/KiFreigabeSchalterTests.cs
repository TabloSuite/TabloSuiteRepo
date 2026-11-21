using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Infrastruktur;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using ENote.WebApp.Services.KiHilfe;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// 21.11.2026: « Assistant (Poser une question) pour tous les rôles » als Schalter in Administration › Réglages – Freigabe
/// ohne Deployment. Gespeichert in <see cref="PlattformEinstellung"/> (eine Zeile), nur der Administrateur darf schalten.
/// </summary>
public class KiFreigabeSchalterTests
{
    private sealed class Uhr : TimeProvider
    {
        public DateTimeOffset Jetzt { get; set; } = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Jetzt;
    }

    private sealed class FakeKiClient : IKiClient
    {
        public int Aufrufe { get; private set; }
        public Task<KiRohAntwort> FrageAsync(KiAnfrage anfrage, CancellationToken ct)
        {
            Aufrufe++;
            return Task.FromResult(new KiRohAntwort("Ouvrez Gestion › Équipe.", KiStopp.Fertig, 10, 0, 0, 20));
        }
    }

    private sealed record Welt(IDbContextFactory<ApplicationDbContext> F, PlattformEinstellungen Einstellungen, KiHilfeDienst Dienst,
        FakeKiClient Client, Uhr Uhr, Guid Admin, Guid Besitzer, Guid Po);

    private static async Task<Welt> WeltAsync(string? schluessel = "sk-test")
    {
        var f = TestDbContextFactory.Create();
        var admin = new AppUser { Id = Guid.NewGuid(), UserName = "admin", FullName = "Admin" };
        var besitzer = new AppUser { Id = Guid.NewGuid(), UserName = "ama", FullName = "Ama" };
        var po = new AppUser { Id = Guid.NewGuid(), UserName = "po", FullName = "Responsable" };
        await using (var db = await f.CreateDbContextAsync())
        {
            var rA = new Role { Name = AppRollen.SuperAdmin, NormalizedName = "SUPERADMIN" };
            var rB = new Role { Name = AppRollen.Besitzer, NormalizedName = "BESITZER" };
            var rP = new Role { Name = AppRollen.ProduktOwner, NormalizedName = "PRODUKTOWNER" };
            db.Roles.AddRange(rA, rB, rP);
            db.Users.AddRange(admin, besitzer, po);
            db.UserRoles.AddRange(new AppUserRole { UserId = admin.Id, RoleId = rA.Id }, new AppUserRole { UserId = besitzer.Id, RoleId = rB.Id },
                new AppUserRole { UserId = po.Id, RoleId = rP.Id });
            await db.SaveChangesAsync();
        }
        var uhr = new Uhr();
        var einstellungen = new PlattformEinstellungen(f, NullLogger<PlattformEinstellungen>.Instance, uhr);
        var client = new FakeKiClient();
        var dienst = new KiHilfeDienst(client, Options.Create(new KiHilfeOptionen { AnthropicApiKey = schluessel, AlleRollen = false }),
            NullLogger<KiHilfeDienst>.Instance, einstellungen);
        return new Welt(f, einstellungen, dienst, client, uhr, admin.Id, besitzer.Id, po.Id);
    }

    [Fact]
    public async Task Schalter_gibt_das_Assistant_fuer_alle_Rollen_frei_und_wieder_zurueck()
    {
        var w = await WeltAsync();
        Assert.False(await w.Dienst.IstVerfuegbarFuerAsync(AppRollen.Besitzer));       // Standard: aus
        Assert.True(await w.Dienst.IstVerfuegbarFuerAsync(AppRollen.ProduktOwner));
        Assert.Equal(KiHilfeDienst.MeldungIndisponible, (await w.Dienst.FrageAsync(new KiRolle(AppRollen.Besitzer), "Question")).Meldung);

        await w.Einstellungen.SetzeKiFuerAlleRollenAsync(w.Admin, true);

        // Sofort, ohne Warten auf den Zwischenspeicher: Menü (synchron) und Seite/Frage (asynchron).
        Assert.True(w.Dienst.IstVerfuegbarFuer(AppRollen.Besitzer));
        Assert.True(w.Dienst.IstVerfuegbarFuer(string.Empty));                           // NavMenu: Laden-Rollen
        Assert.True(await w.Dienst.IstVerfuegbarFuerAsync(AppRollen.Kassierer));
        Assert.True((await w.Dienst.FrageAsync(new KiRolle(AppRollen.Besitzer), "Comment ajouter un employé ?")).Erfolg);
        Assert.Equal(1, w.Client.Aufrufe);
        await using (var db = await w.F.CreateDbContextAsync())
        {
            var e = await db.PlattformEinstellungen.SingleAsync();
            Assert.Equal((PlattformEinstellung.FesteId, true, (Guid?)w.Admin), (e.Id, e.KiFuerAlleRollen, e.GeaendertVonId));
        }

        await w.Einstellungen.SetzeKiFuerAlleRollenAsync(w.Admin, false);
        Assert.False(w.Dienst.IstVerfuegbarFuer(AppRollen.Besitzer));
        Assert.True(w.Dienst.IstVerfuegbarFuer(AppRollen.SuperAdmin));
    }

    [Fact]
    public async Task Ohne_Schluessel_bleibt_das_Assistant_trotz_Schalter_unsichtbar()
    {
        var w = await WeltAsync(schluessel: null);
        await w.Einstellungen.SetzeKiFuerAlleRollenAsync(w.Admin, true);
        Assert.False(w.Dienst.IstVerfuegbarFuer(AppRollen.Besitzer));
        Assert.False(await w.Dienst.IstVerfuegbarFuerAsync(AppRollen.SuperAdmin));
    }

    [Fact]
    public async Task Nur_der_Administrateur_darf_schalten()
    {
        var w = await WeltAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Einstellungen.SetzeKiFuerAlleRollenAsync(w.Besitzer, true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Einstellungen.SetzeKiFuerAlleRollenAsync(w.Po, true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Einstellungen.SetzeKiFuerAlleRollenAsync(Guid.NewGuid(), true));
        Assert.False(await w.Einstellungen.GetKiFuerAlleRollenAsync());
        await using var db = await w.F.CreateDbContextAsync();
        Assert.Empty(await db.PlattformEinstellungen.ToListAsync());
    }

    [Fact]
    public async Task Wert_steht_60_Sekunden_im_Speicher()
    {
        var w = await WeltAsync();
        Assert.False(await w.Einstellungen.GetKiFuerAlleRollenAsync());
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.PlattformEinstellungen.Add(new PlattformEinstellung { KiFuerAlleRollen = true });   // z. B. per Skript geändert
            await db.SaveChangesAsync();
        }

        w.Uhr.Jetzt += TimeSpan.FromSeconds(30);
        Assert.False(await w.Einstellungen.GetKiFuerAlleRollenAsync());                 // noch zwischengespeichert
        w.Uhr.Jetzt += TimeSpan.FromSeconds(31);
        Assert.True(await w.Einstellungen.GetKiFuerAlleRollenAsync());                  // nach 60 s neu gelesen
    }

    private sealed class KaputteFactory : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => throw new InvalidOperationException("Invalid object name 'PlattformEinstellungen'.");
    }

    [Fact]
    public async Task Fehlende_Tabelle_gilt_als_aus_ohne_Fehler()
    {
        // Code vor der Migration deployt: kein Absturz, Assistant bleibt bei Admin und Responsable.
        var einstellungen = new PlattformEinstellungen(new KaputteFactory(), NullLogger<PlattformEinstellungen>.Instance);
        var dienst = new KiHilfeDienst(new FakeKiClient(), Options.Create(new KiHilfeOptionen { AnthropicApiKey = "sk-test" }),
            NullLogger<KiHilfeDienst>.Instance, einstellungen);

        Assert.False(await einstellungen.GetKiFuerAlleRollenAsync());
        Assert.False(dienst.IstVerfuegbarFuer(AppRollen.Besitzer));
        Assert.False(await dienst.IstVerfuegbarFuerAsync(AppRollen.Besitzer));
        Assert.True(await dienst.IstVerfuegbarFuerAsync(AppRollen.SuperAdmin));
    }

    [Fact]
    public void Reiter_Reglages_nur_fuer_den_Administrateur()
    {
        var webApp = Path.Combine(RepoWurzel(), "eNOTE", "eNOTE.WebApp");
        var admin = File.ReadAllText(Path.Combine(webApp, "Components", "Pages", "Admin.razor"));
        Assert.Contains("else if (aktiverTab == \"reglages\" && istSuperAdmin)", admin);
        Assert.Contains("<PlattformEinstellungenAdmin AdminId=\"aktuellerBenutzerId\" />", admin);
        var schalter = File.ReadAllText(Path.Combine(webApp, "Components", "Shared", "PlattformEinstellungenAdmin.razor"));
        Assert.Contains("Label=\"Assistant (Poser une question) pour tous les rôles\"", schalter);
        Assert.Contains("AddSingleton<ENote.WebApp.Services.PlattformEinstellungen>()", File.ReadAllText(Path.Combine(webApp, "Program.cs")));
    }

    private static string RepoWurzel([System.Runtime.CompilerServices.CallerFilePath] string quelle = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(quelle)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "eNOTE", "eNOTE.WebApp", "Program.cs")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo-Wurzel nicht gefunden.");
    }
}
