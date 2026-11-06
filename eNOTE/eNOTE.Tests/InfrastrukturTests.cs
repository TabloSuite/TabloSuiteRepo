using Azure.Storage.Blobs;
using ENote.Core.Options;
using ENote.Core.Services.Storage;
using ENote.WebApp.Data.Seed;
using ENote.WebApp.Services.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ENote.Tests;

public class InfrastrukturTests
{
    private const string SqlMi = "Server=example.database.windows.net;Database=Test;Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False";

    [Theory]
    [InlineData("Development", typeof(LocalMediaStorageService))]
    [InlineData("Production", typeof(AzureBlobMediaStorageService))]
    [InlineData("Staging", typeof(AzureBlobMediaStorageService))]
    public void Umgebung_Waehlt_Speicher_Ohne_Fallback(string umgebung, Type erwartet)
    {
        var env = new TestUmgebung(umgebung);
        var config = Konfiguration(new()
        {
            ["AzureBlobStorage:ServiceUri"] = "https://example.blob.core.windows.net",
            ["DataProtection:KeysPath"] = Path.Combine(env.ContentRootPath, "keys")
        });
        var services = new ServiceCollection();
        services.AddLogging(); services.AddSingleton<IWebHostEnvironment>(env);
        services.AddEnoteInfrastruktur(config, env, SqlMi);
        using var provider = services.BuildServiceProvider();
        Assert.IsType(erwartet, provider.GetRequiredService<IMediaStorageService>());
        if (umgebung != "Development")
            Assert.Same(provider.GetRequiredService<BlobServiceClient>(), provider.GetRequiredService<BlobServiceClient>());
    }

    [Fact]
    public void Production_Ohne_BlobUri_Bricht_Ab_Trotz_Lokalem_Pfad()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddEnoteInfrastruktur(
            Konfiguration(new() { ["AzureBlobStorage:LocalDataRootPath"] = "private" }), new TestUmgebung("Production"), SqlMi));
    }

    [Theory]
    [InlineData("Server=server;Database=db;User Id=user;Password=placeholder")]
    [InlineData("Server=server;Database=db;Integrated Security=True")]
    [InlineData("Server=server;Database=db;Authentication=Active Directory Default")]
    [InlineData(SqlMi + ";TrustServerCertificate=True")]
    [InlineData(SqlMi + ";Encrypt=False")]
    public void Production_Verweigert_Andere_Sql_Authentifizierung(string sql)
        => Assert.Throws<InvalidOperationException>(() => EnoteInfrastruktur.PruefeProduktionsSql(sql));

    [Theory]
    [InlineData("http://example.blob.core.windows.net")]
    [InlineData("https://example.blob.core.windows.net?sig=placeholder")]
    [InlineData("https://example.blob.core.windows.net/container")]
    [InlineData("https://localhost")]
    public void Blob_Verweigert_Unsichere_Endpoints(string uri)
        => Assert.Throws<InvalidOperationException>(() => EnoteInfrastruktur.PruefeBlobKonfiguration(new() { ServiceUri = uri }));

    [Fact]
    public void Private_Dateien_Duerfen_Nicht_In_Wwwroot_Liegen()
    {
        var env = new TestUmgebung("Development");
        Assert.Throws<InvalidOperationException>(() => EnoteInfrastruktur.LokalerMedienPfad(new() { LocalDataRootPath = "wwwroot/media" }, env));
        Assert.Throws<InvalidOperationException>(() => new LocalMediaStorageService(Options.Create(new AzureBlobStorageOptions()),
            new TestUmgebung("Production"), NullLogger<LocalMediaStorageService>.Instance));
    }

    [Fact]
    public void Production_Benoetigt_Persistente_Private_Schluessel()
    {
        var env = new TestUmgebung("Production");
        var config = Konfiguration(new() { ["AzureBlobStorage:ServiceUri"] = "https://example.blob.core.windows.net" });
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddEnoteInfrastruktur(config, env, SqlMi));
        config["DataProtection:KeysPath"] = Path.Combine(env.WebRootPath, "keys");
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddEnoteInfrastruktur(config, env, SqlMi));
    }

    [Fact]
    public void DataProtection_Bleibt_Nach_ProviderNeustart_Lesbar()
    {
        var env = new TestUmgebung("Production");
        var config = Konfiguration(new()
        {
            ["AzureBlobStorage:ServiceUri"] = "https://example.blob.core.windows.net",
            ["DataProtection:KeysPath"] = Path.Combine(env.ContentRootPath, "keys")
        });
        ServiceProvider Provider()
        {
            var services = new ServiceCollection(); services.AddLogging();
            services.AddEnoteInfrastruktur(config, env, SqlMi);
            return services.BuildServiceProvider();
        }
        try
        {
            string geschuetzt;
            using (var provider = Provider())
                geschuetzt = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("PIN-Test").Protect("test-pin");
            using var neu = Provider();
            Assert.Equal("test-pin", neu.GetRequiredService<IDataProtectionProvider>().CreateProtector("PIN-Test").Unprotect(geschuetzt));
        }
        finally { if (Directory.Exists(env.ContentRootPath)) Directory.Delete(env.ContentRootPath, true); }
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("/absolute")]
    [InlineData("global/../../secret")]
    [InlineData("global/abc.svg")]
    [InlineData("global\\file.png")]
    public async Task Medien_Verweigern_Pfadmanipulation(string name)
    {
        var env = new TestUmgebung("Development");
        var storage = new LocalMediaStorageService(Options.Create(new AzureBlobStorageOptions()), env, NullLogger<LocalMediaStorageService>.Instance);
        Assert.Null(await storage.LadeBytesAsync(name));
        Assert.False(await storage.DeleteAsync(name));
        Assert.Throws<ArgumentException>(() => storage.GetPublicUrl(name));
    }

    [Fact]
    public async Task Lokale_Datei_Roundtrip_Und_AppUrl()
    {
        var env = new TestUmgebung("Development");
        var storage = new LocalMediaStorageService(Options.Create(new AzureBlobStorageOptions()), env, NullLogger<LocalMediaStorageService>.Instance);
        try
        {
            using var stream = new MemoryStream([1, 2, 3]);
            var result = await storage.SaveAsync(new() { DateiName = "photo.png", ContentType = "image/png", Inhalt = stream, SalonId = Guid.NewGuid() });
            Assert.True(result.Erfolg); Assert.StartsWith("/media/", result.OeffentlicheUrl);
            Assert.DoesNotContain("%2F", result.OeffentlicheUrl!);
            Assert.Equal(new byte[] { 1, 2, 3 }, await storage.LadeBytesAsync(result.BlobName!));
            Assert.True(await storage.DeleteAsync(result.BlobName!)); Assert.Null(await storage.LadeBytesAsync(result.BlobName!));
        }
        finally { if (Directory.Exists(env.ContentRootPath)) Directory.Delete(env.ContentRootPath, true); }
    }

    [Fact]
    public async Task Upload_Grenze_Gilt_Auch_Ohne_StreamLength()
    {
        using var inhalt = new NichtSuchbarerStream(new byte[MedienDateien.MaxGroesseBytes + 1]);
        Assert.Null(await MedienDateien.LeseUploadAsync(inhalt, default));
    }

    [Fact]
    public async Task Blob_Fehler_Liefert_Keinen_Lokalen_Ersatz()
    {
        var storage = new AzureBlobMediaStorageService(new FehlenderContainer(), Options.Create(new AzureBlobStorageOptions()), NullLogger<AzureBlobMediaStorageService>.Instance);
        using var inhalt = new MemoryStream([1]);
        var result = await storage.SaveAsync(new() { DateiName = "image.png", ContentType = "image/png", Inhalt = inhalt });
        Assert.False(result.Erfolg); Assert.Null(result.BlobName);
    }

    [Theory]
    [InlineData("migrate", true, false)]
    [InlineData("seed", false, true)]
    [InlineData("migrate-and-seed", true, true)]
    public void Administration_Ist_Explizit_Und_Zielgebunden(string modus, bool migrate, bool seed)
    {
        var env = new TestUmgebung("Production"); var config = Konfiguration(new());
        Assert.Null(DatenbankAdministration.Pruefe([], config, env, SqlMi));
        Assert.Throws<InvalidOperationException>(() => DatenbankAdministration.Pruefe([$"--database-admin={modus}"], config, env, SqlMi));
        Assert.Throws<InvalidOperationException>(() => DatenbankAdministration.Pruefe([$"--database-admin={modus}", "--database-target=localhost/Other"], config, env, SqlMi));
        var plan = DatenbankAdministration.Pruefe([$"--database-admin={modus}", "--database-target=example.database.windows.net/Test"], config, env, SqlMi);
        Assert.Equal(migrate, plan!.Migrieren); Assert.Equal(seed, plan.Seed);
    }

    [Fact]
    public void Alter_Startup_Schalter_Fuehrt_Keine_Stille_Migration_Aus()
        => Assert.Throws<InvalidOperationException>(() => DatenbankAdministration.Pruefe([], Konfiguration(new()
            { ["Database:ApplyMigrationsOnStartup"] = "true" }), new TestUmgebung("Production"), SqlMi));

    [Fact]
    public void Development_SeedOnly_Benoetigt_Ebenfalls_Zielbestaetigung()
    {
        var config = Konfiguration(new() { ["Database:SeedOnly"] = "true" });
        var env = new TestUmgebung("Development");
        Assert.Throws<InvalidOperationException>(() => DatenbankAdministration.Pruefe([], config, env, SqlMi));
        var plan = DatenbankAdministration.Pruefe(["--database-target=example.database.windows.net/Test"], config, env, SqlMi);
        Assert.True(plan!.Seed); Assert.False(plan.Migrieren);
    }

    private static IConfigurationRoot Konfiguration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class FehlenderContainer : BlobServiceClient
    {
        public override BlobContainerClient GetBlobContainerClient(string blobContainerName)
            => throw new InvalidOperationException("Container nicht vorhanden");
    }

    private sealed class NichtSuchbarerStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }

    private sealed class TestUmgebung(string name) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "eNOTE.Tests";
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "enote-infra-tests", Guid.NewGuid().ToString("N"));
        public string WebRootPath { get => Path.Combine(ContentRootPath, "wwwroot"); set { } }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
