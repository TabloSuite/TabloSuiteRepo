using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ENote.Tests;

public class TeamServiceTests
{
    [Fact]
    public async Task GetTeam_Erkennt_Besitzer_Ohne_UserManager_Und_Nur_Fuer_Den_Laden()
    {
        var factory = TestDbContextFactory.Create();
        var besitzerRolle = new Role { Id = Guid.NewGuid(), Name = AppRollen.Besitzer, NormalizedName = AppRollen.Besitzer.ToUpperInvariant() };
        var kassiererRolle = new Role { Id = Guid.NewGuid(), Name = AppRollen.Kassierer, NormalizedName = AppRollen.Kassierer.ToUpperInvariant() };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Ama Mensah", Email = "ama@test.invalid" };
        var kassierer = new AppUser { Id = Guid.NewGuid(), FullName = "Awa Koffi", Email = "awa@test.invalid" };
        var fremd = new AppUser { Id = Guid.NewGuid(), FullName = "Fremd", Email = "x@test.invalid" };
        var laden = new Salon { Name = "Laden" };
        var andererLaden = new Salon { Name = "Anderer" };

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Roles.AddRange(besitzerRolle, kassiererRolle);
            db.Users.AddRange(besitzer, kassierer, fremd);
            db.UserRoles.AddRange(
                new AppUserRole { UserId = besitzer.Id, RoleId = besitzerRolle.Id },
                new AppUserRole { UserId = kassierer.Id, RoleId = kassiererRolle.Id },
                new AppUserRole { UserId = fremd.Id, RoleId = besitzerRolle.Id });
            db.UserSalons.AddRange(
                new UserSalon { AppUser = besitzer, Salon = laden, KannFinanzenSehen = true },
                new UserSalon { AppUser = kassierer, Salon = laden, KassenPinHash = "hash" },
                new UserSalon { AppUser = fremd, Salon = andererLaden });
            await db.SaveChangesAsync();
        }

        // UserManager wird für das Lesen des Teams bewusst nicht mehr benötigt.
        var service = new TeamService(factory, null!, NullLogger<TeamService>.Instance);
        var team = await service.GetTeamAsync(laden.Id);

        Assert.Equal(["Ama Mensah", "Awa Koffi"], team.Select(t => t.Name));
        Assert.True(team.Single(t => t.UserId == besitzer.Id).IstBesitzer);
        Assert.False(team.Single(t => t.UserId == kassierer.Id).IstBesitzer);
        Assert.True(team.Single(t => t.UserId == kassierer.Id).HatPin);
    }

    /// <summary>
    /// Fehlersuche 21.11.2026 + Nutzerentscheidung 22.11.: drei « Ama Mensah » im selben Laden waren an der Kasse nicht zu
    /// unterscheiden. Ein gleichnamiges Mitglied (auch der Besitzer, Schreibweise egal) wird abgelehnt – in einem anderen
    /// Laden ist der Name frei.
    /// </summary>
    [Theory]
    [InlineData("Ama", "Mensah")]
    [InlineData("  ama ", "MENSAH")]
    [InlineData("Awa", "Koffi")]
    // Nutzerentscheidung 22.11.2026: Akzente zählen nicht (« Délali » = « Delali »), in beide Richtungen.
    [InlineData("Àma", "Mènsah")]
    [InlineData("Delali", "Agbeko")]
    [InlineData("DÉLALI", "agbéko")]
    [InlineData("Laetitia", "Kpodar")]
    public async Task Gleichnamiges_Mitglied_Im_Selben_Laden_Wird_Abgelehnt(string vorname, string nachname)
    {
        var factory = TestDbContextFactory.Create();
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Ama Mensah", Vorname = "Ama", Nachname = "Mensah" };
        var kassierer = new AppUser { Id = Guid.NewGuid(), FullName = "Awa  Koffi" };
        var mitAkzent = new AppUser { Id = Guid.NewGuid(), FullName = "Délali Agbéko", Vorname = "Délali", Nachname = "Agbéko" };
        var ligatur = new AppUser { Id = Guid.NewGuid(), FullName = "Lætitia Kpodar", Vorname = "Lætitia", Nachname = "Kpodar" };
        var laden = new Salon { Name = "Laden" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.AddRange(besitzer, kassierer, mitAkzent, ligatur);
            db.UserSalons.AddRange(new UserSalon { AppUser = besitzer, Salon = laden }, new UserSalon { AppUser = kassierer, Salon = laden },
                new UserSalon { AppUser = mitAkzent, Salon = laden }, new UserSalon { AppUser = ligatur, Salon = laden });
            await db.SaveChangesAsync();
        }

        // Ohne UserManager: die Prüfung muss vor jeder Konto-Anlage greifen.
        var service = new TeamService(factory, null!, NullLogger<TeamService>.Instance);
        var (erfolg, fehler, konto) = await service.LegeKassiererAnAsync(laden.Id, vorname, nachname, "", "", null, "2468");

        Assert.False(erfolg);
        Assert.Null(konto);
        Assert.Contains("porte déjà ce nom", fehler);
        // Anderer Laden: derselbe Name stört nicht (Prüfung allein, ohne Konto-Anlage)
        var andererLaden = Guid.NewGuid();
        var methode = typeof(TeamService).GetMethod("NameImLadenVergebenAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Assert.False(await (Task<bool>)methode.Invoke(service, [andererLaden, vorname, nachname, CancellationToken.None])!);
    }

    [Theory]
    [InlineData("Délali Agbéko", "delali agbeko")]
    [InlineData("  Ama   MENSAH ", "ama mensah")]
    [InlineData("Lætitia Cœur", "laetitia coeur")]
    [InlineData("Ñoël Ïbrahïm", "noel ibrahim")]
    public void NameFuerVergleich_Ignoriert_Akzente_Schreibweise_Und_Leerzeichen(string name, string erwartet)
        => Assert.Equal(erwartet, TeamService.NameFuerVergleich(name));

    [Fact]
    public void NameFuerVergleich_Unterscheidet_Weiter_Verschiedene_Namen()
    {
        Assert.NotEqual(TeamService.NameFuerVergleich("Délali"), TeamService.NameFuerVergleich("Dalila"));
        Assert.NotEqual(TeamService.NameFuerVergleich("Ama Mensah"), TeamService.NameFuerVergleich("Ama Mensah K."));
    }
}
