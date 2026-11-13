using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Options;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>
/// Gestohlene Zugangsdaten eines Responsable produit: er sieht nur Bilder seiner Schulen
/// und löscht nur eigene Uploads – nie die Medien der ganzen Plattform.
/// </summary>
public class ResponsableMedienRechteTests
{
    [Fact]
    public async Task Responsable_sieht_nur_eigene_Schulen_und_loescht_keine_fremden_Bilder()
    {
        var root = Path.Combine(Path.GetTempPath(), "skoulou-medien-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var db = TestDbContext.CreateInMemory();
            var eigene = new School { Id = Guid.NewGuid(), Name = "École A" };
            var fremde = new School { Id = Guid.NewGuid(), Name = "École B" };
            var po = new AppUser { Id = Guid.NewGuid(), UserName = "po" };
            var lehrer = new AppUser { Id = Guid.NewGuid(), UserName = "lehrer" };
            Bild(db, eigene.Id, lehrer.Id, out var bildEigeneSchule);
            Bild(db, fremde.Id, lehrer.Id, out var bildFremdeSchule);
            Bild(db, eigene.Id, po.Id, out var bildVomPo);
            db.Schools.AddRange(eigene, fremde);
            db.Users.AddRange(po, lehrer);
            db.UserSchools.Add(new UserSchool { AppUserId = po.Id, SchoolId = eigene.Id });
            await db.SaveChangesAsync();
            var service = new AzureBlobMediaLibraryService(db, new Umgebung(root),
                Options.Create(new AzureBlobStorageOptions { Enabled = false, LocalDataRootPath = root }));

            var sichtbar = (await service.GetVisibleImagesAsync(po.Id, [Rollen.ProduktOwner])).Select(b => b.Id).ToList();
            Assert.Contains(bildEigeneSchule, sichtbar);
            Assert.DoesNotContain(bildFremdeSchule, sichtbar);
            Assert.Equal(3, (await service.GetVisibleImagesAsync(Guid.NewGuid(), [Rollen.SuperAdmin])).Count);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteImageAsync(bildEigeneSchule, po.Id, [Rollen.ProduktOwner]));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteImageAsync(bildFremdeSchule, po.Id, [Rollen.ProduktOwner]));
            await service.DeleteImageAsync(bildVomPo, po.Id, [Rollen.ProduktOwner]);
            Assert.Equal(2, db.BlobMediaAssets.Count());
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Bild(TestDbContext db, Guid schule, Guid besitzer, out Guid id)
    {
        id = Guid.NewGuid();
        db.BlobMediaAssets.Add(new BlobMediaAsset { Id = id, OwnerId = besitzer, SchoolId = schule, BlobName = id + ".png",
            MediaType = BlobMediaType.Image, ContentType = "image/png", OriginalFileName = "bild.png" });
    }

    private sealed class Umgebung(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
