using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Options;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class SchuelerVideoZugangTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VeroeffentlichtesVideoOhneAboErfordertAktiveSchulKlassenBerechtigung(bool lehrermaterial)
    {
        var root = Path.Combine(Path.GetTempPath(), "skoulou-video-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var db = TestDbContext.CreateInMemory();
            var schule = new School { Id = Guid.NewGuid(), Name = "École" };
            var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "6e" };
            var student = new AppUser { Id = Guid.NewGuid() };
            var lehrer = new AppUser { Id = Guid.NewGuid() };
            var profil = new StudentProfile { AppUserId = student.Id, SchoolId = schule.Id, CurrentKlasseId = klasse.Id };
            var video = new BlobMediaAsset { Id = Guid.NewGuid(), OwnerId = lehrer.Id, SchoolId = schule.Id,
                KlasseId = klasse.Id, BlobName = "test.mp4", MediaType = BlobMediaType.Video, ContentType = "video/mp4" };
            db.Schools.Add(schule); db.Klassen.Add(klasse); db.Users.AddRange(student, lehrer);
            db.StudentProfiles.Add(profil); db.BlobMediaAssets.Add(video);
            db.UserSchools.Add(new UserSchool { AppUserId = lehrer.Id, SchoolId = schule.Id });
            var lesson = new Lesson { Id = Guid.NewGuid(), KlasseId = klasse.Id, Title = "Leçon", IsPublished = true };
            var support = new TeacherLesson { Id = Guid.NewGuid(), KlasseId = klasse.Id, AuthorId = lehrer.Id,
                Title = "Support", IsPublished = true, VideoAssetId = video.Id };
            if (lehrermaterial) db.TeacherLessons.Add(support);
            else
            {
                db.Lessons.Add(lesson);
                db.LessonMedia.Add(new LessonMedia { LessonId = lesson.Id, MediaType = "video", RelativePath = $"/media-library/videos/{video.Id:D}" });
            }
            await db.SaveChangesAsync();
            await File.WriteAllBytesAsync(Path.Combine(root, video.BlobName), [1, 2, 3]);
            var service = new AzureBlobMediaLibraryService(db, new Umgebung(root),
                Options.Create(new AzureBlobStorageOptions { Enabled = false, LocalDataRootPath = root }));
            var lessonID = lehrermaterial ? support.Id : lesson.Id;
            var offen = await service.OpenVideoAsync(video.Id, student.Id, [Rollen.Schueler], false, lessonID);
            Assert.NotNull(offen); await offen.Content.DisposeAsync();
            Assert.Null(await service.OpenVideoAsync(video.Id, student.Id, [Rollen.Schueler], true, Guid.NewGuid()));
            student.IsBlocked = true; await db.SaveChangesAsync();
            Assert.Null(await service.OpenVideoAsync(video.Id, student.Id, [Rollen.Schueler], true, lessonID));
            student.IsBlocked = false; schule.IsActive = false; await db.SaveChangesAsync();
            Assert.Null(await service.OpenVideoAsync(video.Id, student.Id, [Rollen.Schueler], true, lessonID));
            schule.IsActive = true; profil.IsArchived = true; await db.SaveChangesAsync();
            Assert.Null(await service.OpenVideoAsync(video.Id, student.Id, [Rollen.Schueler], true, lessonID));
            profil.IsArchived = false; lesson.IsPublished = false; support.IsPublished = false; await db.SaveChangesAsync();
            Assert.Null(await service.OpenVideoAsync(video.Id, student.Id, [Rollen.Schueler], true, lessonID));
        }
        finally { Directory.Delete(root, true); }
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
