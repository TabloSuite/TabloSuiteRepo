using SkoulouApp.Core.Options;
using SkoulouApp.WebApp.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class AzureFinanzBelegStorageTests
{
    [Fact]
    public async Task Development_SpeichertUndLiestBelegePrivatAusserhalbVonWwwroot()
    {
        var root = Path.Combine(Path.GetTempPath(), "skoulou-finance-tests", Guid.NewGuid().ToString("N"));
        var environment = new TestEnvironment(root);
        var service = new AzureFinanzBelegStorage(Options.Create(new AzureBlobStorageOptions
        {
            Enabled = false,
            LocalDataRootPath = Path.Combine(root, "storage")
        }), environment);
        try
        {
            var schoolId = Guid.NewGuid();
            var belegId = Guid.NewGuid();
            using var input = new MemoryStream("%PDF-1.4"u8.ToArray());
            var name = await service.SpeichernAsync(schoolId, belegId, input, "application/pdf");
            Assert.Equal($"schools/{schoolId:N}/receipts/{belegId:N}", name);
            Assert.True(File.Exists(Path.Combine(root, "storage", "finance-private", schoolId.ToString("N"), belegId.ToString("N"))));
            await using (var read = await service.OeffnenAsync(name))
            {
                using var output = new MemoryStream();
                await read.CopyToAsync(output);
                Assert.Equal("%PDF-1.4"u8.ToArray(), output.ToArray());
            }
            await service.LoeschenAsync(name);
            Assert.False(File.Exists(Path.Combine(root, "storage", "finance-private", schoolId.ToString("N"), belegId.ToString("N"))));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.OeffnenAsync("../wwwroot/secret"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProductionOhneBlobKonfiguration_NutztKeinenLokalenFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "skoulou-finance-tests", Guid.NewGuid().ToString("N"));
        var service = new AzureFinanzBelegStorage(Options.Create(new AzureBlobStorageOptions
        {
            Enabled = true,
            BlobConnectionString = "",
            LocalDataRootPath = root
        }), new TestEnvironment(root));
        using var input = new MemoryStream("%PDF-1.4"u8.ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SpeichernAsync(Guid.NewGuid(), Guid.NewGuid(), input, "application/pdf"));
        Assert.False(Directory.Exists(root));
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
