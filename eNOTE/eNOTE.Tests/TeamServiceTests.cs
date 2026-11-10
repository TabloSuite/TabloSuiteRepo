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
}
