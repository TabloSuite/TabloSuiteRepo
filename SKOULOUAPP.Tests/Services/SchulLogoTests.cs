using System.Buffers.Binary;
using SkoulouApp.WebApp.Services;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using SkoulouApp.Core.Options;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Tests.Infrastructure;

namespace SkoulouApp.Tests.Services;

public class SchulLogoTests
{
    [Fact]
    public async Task Upload_VerweigertLehrerUndFremdeSchuleVorDateizugriff()
    {
        var factory = new TestApplicationDbContextFactory();
        var user = Guid.NewGuid(); var school = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            var rolle = new Role { Id = Guid.NewGuid(), Name = Rollen.Lehrer };
            db.Users.Add(new AppUser { Id = user }); db.Roles.Add(rolle);
            db.UserRoles.Add(new AppUserRole { UserId = user, RoleId = rolle.Id });
            db.Schools.Add(new School { Id = school, Name = "École" });
            db.UserSchools.Add(new UserSchool { AppUserId = user, SchoolId = school });
            await db.SaveChangesAsync();
        }
        var service = new SchulLogoService(factory, new Umgebung(), Options.Create(new AzureBlobStorageOptions()));
        using var datei = new MemoryStream();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.HochladenAsync(user, school, datei));
        await using (var db = factory.CreateDbContext())
        {
            (await db.Roles.SingleAsync()).Name = Rollen.SchulLeiter;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.HochladenAsync(user, Guid.NewGuid(), datei));
        await using var kontrolle = factory.CreateDbContext();
        Assert.Empty(await kontrolle.BlobMediaAssets.ToListAsync());
    }

    private sealed class Umgebung : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void EchtesPng_WirdAkzeptiert()
    {
        var bild = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        Assert.Equal("image/png", SchulLogoPruefung.Pruefen(bild));
    }

    [Fact]
    public void UebergrosseDimensionen_WerdenVorDemDecodierenAbgewiesen()
    {
        var bild = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        BinaryPrimitives.WriteInt32BigEndian(bild.AsSpan(16, 4), 50000);
        Assert.Throws<InvalidOperationException>(() => SchulLogoPruefung.Pruefen(bild));
    }

    [Fact]
    public void UnechteDateienUndGroessenlimit_WerdenAbgewiesen()
    {
        Assert.Throws<InvalidOperationException>(() => SchulLogoPruefung.Pruefen("<svg></svg>"u8.ToArray()));
        Assert.Throws<InvalidOperationException>(() => SchulLogoPruefung.Pruefen([]));
        Assert.Throws<InvalidOperationException>(() => SchulLogoPruefung.Pruefen(new byte[SchulLogoPruefung.MaxBytes + 1]));
        Assert.Throws<InvalidOperationException>(() => SchulLogoPruefung.Pruefen([255, 216, 255, 217]));
    }
}
