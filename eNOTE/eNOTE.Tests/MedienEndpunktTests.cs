using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Infrastruktur;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Storage;
using ENote.WebApp.Services.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ENote.Tests;

public class MedienEndpunktTests
{
    [Fact]
    public async Task Private_CatchAll_Route_Verhindert_Anonyme_Und_Fremde_Abrufe()
    {
        var factory = TestDbContextFactory.Create();
        var user = new AppUser { Id = Guid.NewGuid() }; var fremder = new AppUser { Id = Guid.NewGuid() };
        var salon = new Salon { Name = "A" };
        var asset = new BlobMediaAsset { SalonId = salon.Id, BlobName = MedienDateien.NeuerName(salon.Id, "photo.png") };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(fremder); db.UserSalons.Add(new UserSalon { AppUser = user, Salon = salon });
            db.BlobMediaAssets.Add(asset); await db.SaveChangesAsync();
        }
        // Reiner lokaler HTTP-Testhost, kein Browser, keine echten Storage-/SQL-Verbindungen.
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(factory); builder.Services.AddScoped<MedienZugriff>();
        var storage = new TestMedien(); builder.Services.AddSingleton<IMediaStorageService>(storage);
        builder.Services.AddAuthentication("Tests").AddScheme<AuthenticationSchemeOptions, TestAnmeldung>("Tests", _ => { });
        builder.Services.AddAuthorization();
        await using var app = builder.Build(); app.MapEnoteMedien(); await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var url = MedienDateien.Url(asset.BlobName);
            using var anonym = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Unauthorized, anonym.StatusCode); Assert.Equal(0, storage.LeseAnzahl);
            client.DefaultRequestHeaders.Add("X-Test-User", fremder.Id.ToString());
            using var fremd = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.NotFound, fremd.StatusCode); Assert.Equal(0, storage.LeseAnzahl);
            client.DefaultRequestHeaders.Remove("X-Test-User"); client.DefaultRequestHeaders.Add("X-Test-User", user.Id.ToString());
            using var erlaubt = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, erlaubt.StatusCode); Assert.Equal(1, storage.LeseAnzahl);
            Assert.Equal("image/png", erlaubt.Content.Headers.ContentType!.MediaType);
            Assert.True(erlaubt.Headers.CacheControl!.NoStore); Assert.True(erlaubt.Headers.CacheControl.Private);
            Assert.Equal("nosniff", erlaubt.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal(new byte[] { 1, 2, 3 }, await erlaubt.Content.ReadAsByteArrayAsync());
        }
        finally { await app.StopAsync(); }
    }

    private sealed class TestMedien : IMediaStorageService
    {
        public int LeseAnzahl { get; private set; }
        public Task<byte[]?> LadeBytesAsync(string blobName, CancellationToken cancellationToken = default)
        { LeseAnzahl++; return Task.FromResult<byte[]?>([1, 2, 3]); }
        public string GetPublicUrl(string blobName) => MedienDateien.Url(blobName);
        public Task<MediaSaveResult> SaveAsync(MediaSaveRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestAnmeldung(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers["X-Test-User"], out var userId)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
