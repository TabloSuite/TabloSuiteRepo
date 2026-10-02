using SkoulouApp.Core.Options;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Data;
using SkoulouApp.WebApp.Models.Auth;
using SkoulouApp.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace SkoulouApp.Tests.Services;

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selbstregistrierung_NachErfolgVersendetGenauEineWillkommensmail(bool director)
    {
        var factory = new TestApplicationDbContextFactory();
        var email = new FakeEmailService();
        var manager = TestUserManager.Create();
        manager.OnUserCreated = user =>
        {
            using var db = factory.CreateDbContext();
            db.Users.Add(user);
            db.SaveChanges();
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            if (!director) db.Schools.Add(new School { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "École élève", IsActive = true });
            db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = director ? Rollen.SchulLeiter : Rollen.Schueler });
            await db.SaveChangesAsync();
        }
        var service = new AuthService(factory.CreateDbContext(), factory, manager, null!, email,
            postmarkOptions: Options.Create(new PostmarkOptions { Enabled = false }),
            configuration: new ConfigurationBuilder().Build(), logger: NullLogger<AuthService>.Instance);
        var input = new RegisterInputModel
        {
            Vorname = "Afi", Nachname = "Mensah", Email = "afi@example.org", Password = "Secret123!",
            AgbAccepted = true, SchulName = "Nouvelle école", SchulStadt = "Lomé",
            SelectedSchoolId = Guid.Parse("11111111-1111-1111-1111-111111111111")
        };

        var result = director ? await service.RegisterSchulLeiterAsync(input) : await service.RegisterStudentAsync(input);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var sent = Assert.Single(email.Gesendet);
        Assert.Equal(input.Email, sent.Recipient.Email);
        Assert.False(string.IsNullOrWhiteSpace(sent.Message.TextBody));
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
