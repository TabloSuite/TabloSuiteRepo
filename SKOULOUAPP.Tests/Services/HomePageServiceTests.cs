using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Schools;
using HalloApp.Core.Services;
using HalloApp.Tests.Infrastructure;
using Xunit;

namespace HalloApp.Tests.Services;

/// <summary>
/// Tests für <see cref="HomePageService"/>, der Katalog, Schulen und
/// Dashboard-Kennzahlen bündelt.
/// </summary>
public class HomePageServiceTests
{
    [Fact]
    public async Task GetCourseCatalogAsync_LiefertKatalogAusUnitOfWork()
    {
        // Arrange
        var catalog = new List<Klasse>
        {
            new() { Id = Guid.NewGuid(), Code = "A1.1", Title = "Anfänger" },
            new() { Id = Guid.NewGuid(), Code = "A2.1", Title = "Grundstufe" }
        };

        var unitOfWork = new FakeUnitOfWork(catalog, new List<School>());
        var service = new HomePageService(unitOfWork, CreateDashboardService());

        // Act
        var result = await service.GetCourseCatalogAsync();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.Code == "A1.1");
    }

    [Fact]
    public async Task GetSchoolsAsync_LiefertSchulenAusUnitOfWork()
    {
        // Arrange
        var schools = new List<School>
        {
            new() { Id = Guid.NewGuid(), Name = "Schule A" }
        };

        var unitOfWork = new FakeUnitOfWork(new List<Klasse>(), schools);
        var service = new HomePageService(unitOfWork, CreateDashboardService());

        // Act
        var result = await service.GetSchoolsAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("Schule A", result[0].Name);
    }

    private static DashboardService CreateDashboardService()
    {
        var db = TestDbContext.CreateInMemory();
        var userManager = TestUserManager.Create(Array.Empty<AppUser>());
        return new DashboardService(db, userManager, new FakeMediaStorageService());
    }
}
