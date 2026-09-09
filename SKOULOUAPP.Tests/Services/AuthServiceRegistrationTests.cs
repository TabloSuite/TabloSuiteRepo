using HalloApp.Core.Options;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Tests.Infrastructure;
using HalloApp.WebApp.Data;
using HalloApp.WebApp.Models.Auth;
using HalloApp.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HalloApp.Tests.Services;

public class AuthServiceRegistrationTests
{
    private static AuthService CreateService(TestUserManager? userManager = null, AppUser? persistedUser = null)
    {
        var factory = new TestApplicationDbContextFactory();
        var db = factory.CreateDbContext();
        if (persistedUser is not null)
        {
            db.Users.Add(persistedUser);
            db.SaveChanges();
        }

        var configuration = new ConfigurationBuilder().Build();
        var postmark = Options.Create(new PostmarkOptions { Enabled = false });

        return new AuthService(
            db,
            factory,
            userManager ?? TestUserManager.Create(),
            signInManager: null!,
            new FakeEmailService(),
            postmark,
            configuration,
            NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task RegisterStudentAsync_WithoutTermsAcceptance_IsRejected()
    {
        var result = await CreateService().RegisterStudentAsync(new RegisterInputModel());

        Assert.False(result.Succeeded);
        Assert.Contains("conditions d'utilisation", result.ErrorMessage);
    }

    [Fact]
    public async Task RegisterSchulLeiterAsync_WithoutTermsAcceptance_IsRejectedBeforeSchoolCreation()
    {
        var service = CreateService();

        var result = await service.RegisterSchulLeiterAsync(new RegisterInputModel { SchulName = "École sans conditions" });

        Assert.False(result.Succeeded);
        Assert.Contains("conditions d'utilisation", result.ErrorMessage);
    }

    [Fact]
    public async Task RequiresTermsAcceptanceAsync_WithoutCurrentAcceptance_ReturnsTrue()
    {
        var user = new AppUser { Id = Guid.NewGuid() };
        var service = CreateService(TestUserManager.Create([user]), user);

        var requiresAcceptance = await service.RequiresTermsAcceptanceAsync(user.Id);

        Assert.True(requiresAcceptance);
    }

    [Fact]
    public async Task AcceptTermsAsync_StoresCurrentVersionAndUnblocksTermsRequirement()
    {
        var user = new AppUser { Id = Guid.NewGuid() };
        var service = CreateService(TestUserManager.Create([user]), user);

        var accepted = await service.AcceptTermsAsync(user.Id);

        Assert.True(accepted);
        Assert.False(await service.RequiresTermsAcceptanceAsync(user.Id));
    }
}
