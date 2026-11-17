using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data.Seed;

namespace Skoulou.Tests.Plattform;

/// <summary>Start-Seed: Rollen und Einstellungen immer, Admin nur falls er fehlt – nie ändern.</summary>
public class StartSeedTests
{
    [Fact]
    public async Task Ohne_Konfiguration_Rollen_und_Einstellungen_aber_kein_Konto()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        await using var db = await u.Db.CreateDbContextAsync();
        Assert.Equal(Rollen.Alle.Count, await db.Roles.CountAsync());
        var einstellung = await db.PlattformEinstellungen.SingleAsync();
        Assert.Equal(5_000, einstellung.JahrespreisProSchueler);
        Assert.Equal(30, einstellung.ZahlungszielTage);
        Assert.Equal(StartSeed.AgbStartVersion, einstellung.AgbVersion);
        Assert.Equal(0, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Admin_wird_einmal_angelegt_und_danach_nie_geaendert()
    {
        await using var u = new TestUmgebung();
        u.Seed.SuperAdminEmail = "admin@exemple.tg";
        u.Seed.SuperAdminPasswort = "Start2026x";
        u.Seed.SuperAdminIdentifiant = "AdminTest";
        await u.SeedAsync();

        string? hashVorher;
        await using (var db = await u.Db.CreateDbContextAsync())
        {
            var admin = await db.Users.SingleAsync();
            Assert.Equal("AdminTest", admin.UserName);
            Assert.Equal(Rollen.SuperAdmin, admin.Rolle);
            Assert.Equal("Administrateur", admin.Vorname);
            Assert.True(admin.IstHauptAdmin); // E7b: Start-Admin = Hauptadministrator
            hashVorher = admin.PasswordHash;
        }

        u.Seed.SuperAdminPasswort = "Ganz4nders";
        u.Seed.SuperAdminName = "Autre Nom";
        await u.SeedAsync();
        await using (var db = await u.Db.CreateDbContextAsync())
        {
            var admin = await db.Users.SingleAsync();
            Assert.Equal(hashVorher, admin.PasswordHash);
            Assert.Equal("Administrateur", admin.Vorname);
            Assert.Equal(1, await db.PlattformEinstellungen.CountAsync());
        }
        var users = u.Hole<UserManager<Konto>>();
        Assert.True(await users.IsInRoleAsync((await users.FindByNameAsync("admintest"))!, Rollen.SuperAdmin));
    }

    [Fact]
    public async Task Ohne_Identifiant_ist_die_Email_der_Name()
    {
        await using var u = new TestUmgebung();
        u.Seed.SuperAdminEmail = "chef@exemple.tg";
        u.Seed.SuperAdminPasswort = "Start2026x";
        await u.SeedAsync();
        await using var db = await u.Db.CreateDbContextAsync();
        Assert.Equal("chef@exemple.tg", (await db.Users.SingleAsync()).UserName);
    }

    [Fact]
    public async Task Vorhandenes_Identifiant_ohne_Adminrolle_bricht_ab()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        await u.KontoAsync("chef", Rollen.Lehrer, (await u.SchuleAsync()).Id);
        u.Seed.SuperAdminEmail = "chef@exemple.tg";
        u.Seed.SuperAdminPasswort = "Start2026x";
        u.Seed.SuperAdminIdentifiant = "chef";
        await Assert.ThrowsAsync<InvalidOperationException>(u.SeedAsync);
        await using var db = await u.Db.CreateDbContextAsync();
        Assert.Equal(Rollen.Lehrer, (await db.Users.SingleAsync()).Rolle);
    }
}
