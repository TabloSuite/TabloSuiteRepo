using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Models.Admin;
using SkoulouApp.Core.Options;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>Anmeldung per Identifiant; E-Mail nur Kontaktadresse (seit 10.11.2026).</summary>
public class IdentifiantTests
{
    [Theory]
    [InlineData("Ama Mensah", "ama.mensah")]
    [InlineData("  Éléonore  D'Almeida ", "eleonore.d.almeida")]
    [InlineData("KOFFI-Agbéko", "koffi-agbeko")]
    [InlineData("..x@y!!", "xy")]
    public void Normalisieren_LiefertKleinbuchstabenOhneAkzente(string eingabe, string erwartet)
        => Assert.Equal(erwartet, IdentifiantRegel.Normalisieren(eingabe));

    [Theory]
    [InlineData("ama.mensah", true)]
    [InlineData("a1", false)]
    [InlineData("ama mensah", false)]
    [InlineData("Ama.Mensah", false)]
    [InlineData(".ama", false)]
    public void IstGueltig_PrueftFormat(string identifiant, bool erwartet)
        => Assert.Equal(erwartet, IdentifiantRegel.IstGueltig(identifiant));

    [Fact]
    public async Task SaveUserAsync_SchlaegtEindeutigesIdentifiantVorUndErlaubtGemeinsameEmail()
    {
        using var db = TestDbContext.CreateInMemory();
        var schoolA = new School { Id = Guid.NewGuid(), Name = "École A" };
        var schoolB = new School { Id = Guid.NewGuid(), Name = "École B" };
        db.Schools.AddRange(schoolA, schoolB);
        await db.SaveChangesAsync();
        var manager = TestUserManager.Create([]);
        manager.OnUserCreated = user => { db.Users.Add(user); db.SaveChanges(); };
        var service = new DashboardService(db, manager, new FakeMediaStorageService());
        UserFormModel Lehrer(Guid schule) => new()
        {
            Vorname = "Ama",
            Nachname = "Mensah",
            Email = "ama@example.org",
            RoleName = Rollen.Lehrer,
            Password = "Start123!",
            SchoolIds = [schule]
        };

        // Dieselbe Lehrkraft an zwei Schulen: zwei Konten, gleiche Kontakt-E-Mail, eigene Identifiants.
        var erstes = Lehrer(schoolA.Id);
        Assert.True((await service.SaveUserAsync(erstes)).Succeeded);
        var zweites = Lehrer(schoolB.Id);
        Assert.True((await service.SaveUserAsync(zweites)).Succeeded);

        Assert.Equal("ama.mensah", erstes.Identifiant);
        Assert.Equal("ama.mensah2", zweites.Identifiant);
        var konten = await db.Users.Where(user => user.Email == "ama@example.org").ToListAsync();
        Assert.Equal(2, konten.Count);
    }

    [Fact]
    public async Task SaveUserAsync_LehntVergebenesIdentifiantAbUndVerlangtEmailFuerDirektion()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "École A" };
        db.Schools.Add(school);
        db.Users.Add(new AppUser { Id = Guid.NewGuid(), UserName = "kofi", NormalizedUserName = "KOFI" });
        await db.SaveChangesAsync();
        var service = new DashboardService(db, TestUserManager.Create([]), new FakeMediaStorageService());

        var vergeben = await service.SaveUserAsync(new UserFormModel
        {
            Vorname = "Kofi", Nachname = "Agbo", Identifiant = "Kofi", RoleName = Rollen.Schueler,
            Password = "Start123!", SchoolIds = [school.Id]
        });
        Assert.False(vergeben.Succeeded);
        Assert.Equal(IdentifiantRegel.MeldungVergeben, vergeben.Message);

        var direktionOhneEmail = await service.SaveUserAsync(new UserFormModel
        {
            Vorname = "Afi", Nachname = "Directrice", RoleName = Rollen.SchulLeiter,
            Password = "Start123!", SchoolIds = [school.Id]
        });
        Assert.False(direktionOhneEmail.Succeeded);
        Assert.Contains("adresse e-mail", direktionOhneEmail.Message);

        var schuelerOhneEmail = await service.SaveUserAsync(new UserFormModel
        {
            Vorname = "Komi", Nachname = "Adjovi", RoleName = Rollen.Schueler,
            Password = "Start123!", SchoolIds = [school.Id]
        });
        Assert.True(schuelerOhneEmail.Succeeded, schuelerOhneEmail.Message);
    }

    [Fact]
    public async Task PasswortReset_NenntAlleKontenEinerGemeinsamenEmail()
    {
        var email = new FakeEmailService();
        var erstes = new AppUser { Id = Guid.NewGuid(), UserName = "ama.mensah", Email = "ama@example.org", Vorname = "Ama", FullName = "Ama Mensah" };
        var zweites = new AppUser { Id = Guid.NewGuid(), UserName = "ama.mensah2", Email = "ama@example.org", Vorname = "Ama", FullName = "Ama Mensah" };
        var manager = TestUserManager.Create([erstes, zweites]);
        manager.PasswordResetToken = "jeton";
        var factory = new TestApplicationDbContextFactory();
        await using (var db = factory.CreateDbContext())
        {
            foreach (var konto in new[] { erstes, zweites })
            {
                db.Users.Add(new AppUser { Id = konto.Id, UserName = konto.UserName, NormalizedUserName = konto.UserName!.ToUpperInvariant(),
                    Email = konto.Email, NormalizedEmail = konto.Email!.ToUpperInvariant(), Vorname = "Ama", FullName = "Ama Mensah" });
            }
            await db.SaveChangesAsync();
        }
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = "https://test.local" })
            .Build();
        var service = new AuthService(factory.CreateDbContext(), factory, manager, null!, email,
            Options.Create(new PostmarkOptions()), configuration, NullLogger<AuthService>.Instance);

        await service.SendPasswordResetAsync("AMA@example.org");

        var html = Assert.Single(email.Gesendet).Message.HtmlBody;
        Assert.Contains("ama.mensah</strong>", html);
        Assert.Contains("ama.mensah2</strong>", html);
        Assert.Contains($"reset-password?id={erstes.Id}", html);
        Assert.Contains($"reset-password?id={zweites.Id}", html);
    }
}
