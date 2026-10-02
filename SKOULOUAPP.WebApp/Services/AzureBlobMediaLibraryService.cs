using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.Interfaces;
using SkoulouApp.Core.Models.Media;
using SkoulouApp.Core.Options;
using SkoulouApp.Core.Services.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace SkoulouApp.WebApp.Services;

/// <summary>
/// Speichert Bilder in einem privaten Azure-Blob-Container und setzt die
/// Sichtbarkeit über die zugehörigen Datenbankmetadaten durch.
/// </summary>
public sealed class AzureBlobMediaLibraryService(
    ISkoulouAppDbContext dbContext,
    IWebHostEnvironment environment,
    IOptions<AzureBlobStorageOptions> options) : IBlobMediaLibraryService
{
    private const int UploadChunkSizeBytes = 4 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp"
    };

    private static readonly HashSet<string> AllowedVideoContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "video/mp4", "video/quicktime"
    };

    private static readonly TimeSpan MaxVideoDuration = TimeSpan.FromHours(1);

    private static readonly IReadOnlyDictionary<string, string> ImageContentTypeByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp"
    };

    private static readonly IReadOnlyDictionary<string, string> VideoContentTypeByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/mp4",
        [".mov"] = "video/quicktime"
    };

    private static readonly StorageTransferOptions UploadTransferOptions = new()
    {
        InitialTransferSize = UploadChunkSizeBytes,
        MaximumTransferSize = UploadChunkSizeBytes,
        MaximumConcurrency = 4
    };

    private readonly AzureBlobStorageOptions storageOptions = options.Value;
    private const string DefaultLocalMediaLibraryRoot = "wwwroot/media-library";
    private const string BlobStorageRootPrefix = "SkoulouAppStorage";

    public async Task<IReadOnlyList<BlobMediaAssetItem>> GetVisibleImagesAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.BlobMediaAssets
            .AsNoTracking()
            .Include(asset => asset.School)
            .Include(asset => asset.Owner)
            .Include(asset => asset.Klasse)
            .Where(asset => asset.MediaType == BlobMediaType.Image && asset.SchoolId != null)
            .AsQueryable();

        query = await ApplyVisibilityAsync(query, userId, roles, cancellationToken);

        var assets = await query
            .OrderByDescending(asset => asset.ErstelltAmUtc)
            .ToListAsync(cancellationToken);
        return assets.Select(ToItem).ToList();
    }

    public async Task<IReadOnlyList<BlobMediaAssetItem>> GetVisibleVideosAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.BlobMediaAssets
            .AsNoTracking()
            .Include(asset => asset.School)
            .Include(asset => asset.Owner)
            .Include(asset => asset.Klasse)
            .Where(asset => asset.MediaType == BlobMediaType.Video && asset.SchoolId != null)
            .AsQueryable();

        query = await ApplyVisibilityAsync(query, userId, roles, cancellationToken);

        var assets = await query
            .OrderByDescending(asset => asset.ErstelltAmUtc)
            .ToListAsync(cancellationToken);
        return assets.Select(ToItem).ToList();
    }

    public async Task<BlobMediaAssetItem> UploadImageAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        Guid schoolId,
        Guid? klasseId,
        Stream content,
        string originalFileName,
        string displayName,
        string contentType,
        string altText,
        CancellationToken cancellationToken = default)
        => await UploadImageCoreAsync(userId, roles, schoolId, klasseId, content, originalFileName, displayName, contentType, altText, false, cancellationToken);

    public async Task<BlobMediaAssetItem> UploadProfileImageAsync(Guid userId, Guid? schoolId, Stream content, string originalFileName, string contentType, CancellationToken cancellationToken = default)
        => await UploadImageCoreAsync(userId, [], schoolId, null, content, originalFileName, Path.GetFileNameWithoutExtension(originalFileName), contentType, "Photo de profil", true, cancellationToken);

    private async Task<BlobMediaAssetItem> UploadImageCoreAsync(
        Guid userId, IReadOnlyCollection<string> roles, Guid? schoolId, Guid? klasseId, Stream content,
        string originalFileName, string displayName, string contentType, string altText, bool profileImage,
        CancellationToken cancellationToken)
    {
        var normalizedContentType = NormalizeImageContentType(contentType, originalFileName);
        ValidateImage(normalizedContentType, altText);

        if (!(profileImage
            ? schoolId.HasValue
                ? await dbContext.UserSchools.AnyAsync(link => link.AppUserId == userId && link.SchoolId == schoolId.Value, cancellationToken)
                : await dbContext.Users.AnyAsync(owner => owner.Id == userId, cancellationToken)
                    && !await dbContext.UserSchools.AnyAsync(link => link.AppUserId == userId, cancellationToken)
            : schoolId.HasValue && await CanUploadToSchoolAsync(userId, roles, schoolId.Value, cancellationToken)))
        {
            throw new UnauthorizedAccessException("Vous n’êtes pas autorisé à ajouter une image pour cette école.");
        }

        if (klasseId.HasValue && !await dbContext.Klassen.AnyAsync(klasse => klasse.Id == klasseId.Value && klasse.SchoolId == schoolId, cancellationToken))
        {
            throw new InvalidOperationException("La classe choisie n'appartient pas à cette école.");
        }

        string blobName;
        if (storageOptions.Enabled)
        {
            ValidateConfiguration();
            blobName = profileImage && !schoolId.HasValue
                ? $"{BlobStorageRootPrefix}/profiles/{userId:N}/{Guid.NewGuid():N}{Path.GetExtension(originalFileName).ToLowerInvariant()}"
                : BuildBlobStoragePath(schoolId!.Value, klasseId, originalFileName, displayName, "images");
            var blob = GetContainerClient().GetBlobClient(blobName);
            await blob.UploadAsync(content, new BlobUploadOptions
            {
                TransferOptions = UploadTransferOptions,
                HttpHeaders = new BlobHttpHeaders { ContentType = normalizedContentType }
            }, cancellationToken);
        }
        else
        {
            blobName = profileImage && !schoolId.HasValue
                ? $"profiles/{userId:N}/{Guid.NewGuid():N}{Path.GetExtension(originalFileName).ToLowerInvariant()}"
                : BuildLocalMediaPath(schoolId!.Value, klasseId, originalFileName, displayName, "images");
            var physicalPath = GetLocalMediaPath(blobName);
            Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
            await using var fileStream = File.Create(physicalPath);
            await content.CopyToAsync(fileStream, cancellationToken);
        }

        var asset = new BlobMediaAsset
        {
            SchoolId = schoolId,
            OwnerId = userId,
            KlasseId = klasseId,
            BlobName = blobName,
            OriginalFileName = Path.GetFileName(originalFileName),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(originalFileName) : displayName.Trim(),
            ContentType = normalizedContentType,
            Length = content.CanSeek ? content.Length : 0,
            AltText = altText.Trim()
        };

        dbContext.BlobMediaAssets.Add(asset);
        await dbContext.SaveChangesAsync(cancellationToken);

        var hydrated = await dbContext.BlobMediaAssets
            .AsNoTracking()
            .Include(item => item.School)
            .Include(item => item.Owner)
            .Include(item => item.Klasse)
            .SingleAsync(item => item.Id == asset.Id, cancellationToken);
        return ToItem(hydrated);
    }

    public async Task<BlobMediaDownload?> OpenImageAsync(
        Guid assetId,
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.BlobMediaAssets.AsNoTracking().AsQueryable();
        query = await ApplyVisibilityAsync(query, userId, roles, cancellationToken);
        query = query.Union(dbContext.BlobMediaAssets.AsNoTracking().Where(asset => asset.Id == assetId
            && asset.OwnerId == userId && dbContext.Users.Any(owner => owner.Id == userId && owner.ProfilBildAssetId == asset.Id)));
        if (roles.Contains(Rollen.Schueler) && await CanStudentOpenImageAsync(assetId, userId, cancellationToken))
        {
            query = query.Union(dbContext.BlobMediaAssets.AsNoTracking().Where(asset => asset.Id == assetId));
        }
        var asset = await query.SingleOrDefaultAsync(item => item.Id == assetId && item.MediaType == BlobMediaType.Image, cancellationToken);
        if (asset is null)
        {
            return null;
        }

        if (!storageOptions.Enabled)
        {
            var physicalPath = GetLocalMediaPath(asset.BlobName);
            if (!File.Exists(physicalPath))
            {
                return null;
            }

            return new BlobMediaDownload
            {
                Content = File.OpenRead(physicalPath),
                ContentType = asset.ContentType,
                FileName = asset.OriginalFileName
            };
        }

        ValidateConfiguration();
        var download = await GetContainerClient().GetBlobClient(asset.BlobName).DownloadStreamingAsync(cancellationToken: cancellationToken);
        return new BlobMediaDownload
        {
            Content = download.Value.Content,
            ContentType = asset.ContentType,
            FileName = asset.OriginalFileName
        };
    }

    public async Task<BlobMediaAssetItem> UploadVideoAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        Guid schoolId,
        Guid? klasseId,
        Stream content,
        string originalFileName,
        string displayName,
        string contentType,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var normalizedContentType = NormalizeVideoContentType(contentType, originalFileName);
        ValidateVideo(normalizedContentType, duration);

        if (!await CanUploadToSchoolAsync(userId, roles, schoolId, cancellationToken))
        {
            throw new UnauthorizedAccessException("Vous n’êtes pas autorisé à ajouter une vidéo pour cette école.");
        }

        if (klasseId.HasValue && !await dbContext.Klassen.AnyAsync(klasse => klasse.Id == klasseId.Value && klasse.SchoolId == schoolId, cancellationToken))
        {
            throw new InvalidOperationException("La classe choisie n'appartient pas à cette école.");
        }

        string blobName;
        if (storageOptions.Enabled)
        {
            ValidateConfiguration();
            blobName = BuildBlobStoragePath(schoolId, klasseId, originalFileName, displayName, "videos");
            var blob = GetContainerClient().GetBlobClient(blobName);
            await blob.UploadAsync(content, new BlobUploadOptions
            {
                TransferOptions = UploadTransferOptions,
                HttpHeaders = new BlobHttpHeaders { ContentType = normalizedContentType }
            }, cancellationToken);
        }
        else
        {
            blobName = BuildLocalMediaPath(schoolId, klasseId, originalFileName, displayName, "videos");
            var physicalPath = GetLocalMediaPath(blobName);
            Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
            await using var fileStream = File.Create(physicalPath);
            await content.CopyToAsync(fileStream, cancellationToken);
        }

        var asset = new BlobMediaAsset
        {
            SchoolId = schoolId,
            OwnerId = userId,
            KlasseId = klasseId,
            BlobName = blobName,
            OriginalFileName = Path.GetFileName(originalFileName),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(originalFileName) : displayName.Trim(),
            ContentType = normalizedContentType,
            Length = content.CanSeek ? content.Length : 0,
            AltText = string.Empty,
            MediaType = BlobMediaType.Video,
            VideoDuration = duration
        };

        dbContext.BlobMediaAssets.Add(asset);
        await dbContext.SaveChangesAsync(cancellationToken);

        var hydrated = await dbContext.BlobMediaAssets
            .AsNoTracking()
            .Include(item => item.School)
            .Include(item => item.Owner)
            .Include(item => item.Klasse)
            .SingleAsync(item => item.Id == asset.Id, cancellationToken);
        return ToItem(hydrated);
    }

    private static string NormalizeImageContentType(string contentType, string originalFileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType) && AllowedContentTypes.Contains(contentType))
        {
            return contentType.Trim();
        }

        var extension = Path.GetExtension(originalFileName);
        return ImageContentTypeByExtension.TryGetValue(extension, out var mapped)
            ? mapped
            : contentType?.Trim() ?? string.Empty;
    }

    private static string NormalizeVideoContentType(string contentType, string originalFileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType) && AllowedVideoContentTypes.Contains(contentType))
        {
            return contentType.Trim();
        }

        var extension = Path.GetExtension(originalFileName);
        return VideoContentTypeByExtension.TryGetValue(extension, out var mapped)
            ? mapped
            : contentType?.Trim() ?? string.Empty;
    }

    public async Task<BlobMediaDownload?> OpenVideoAsync(
        Guid assetId,
        Guid userId,
        IReadOnlyCollection<string> roles,
        bool hasActiveSubscription,
        Guid? lessonId = null,
        CancellationToken cancellationToken = default)
    {
        var isPrivilegedUser = roles.Any(role => role is Rollen.SuperAdmin or Rollen.ProduktOwner or Rollen.SchulLeiter or Rollen.Koordinator or Rollen.Lehrer);
        var canOpenPublishedTeacherLessonVideo = roles.Contains(Rollen.Schueler)
            && await CanStudentOpenPublishedTeacherLessonVideoAsync(assetId, userId, lessonId, cancellationToken);
        var canOpenPublishedLessonVideo = !canOpenPublishedTeacherLessonVideo
            && roles.Contains(Rollen.Schueler)
            && hasActiveSubscription
            && await CanStudentOpenPublishedLessonVideoAsync(assetId, userId, lessonId, cancellationToken);

        if (!hasActiveSubscription && !isPrivilegedUser && !canOpenPublishedTeacherLessonVideo)
        {
            return null;
        }

        var query = dbContext.BlobMediaAssets.AsNoTracking().AsQueryable();
        if (!canOpenPublishedTeacherLessonVideo && !canOpenPublishedLessonVideo)
        {
            query = await ApplyVisibilityAsync(query, userId, roles, cancellationToken);
        }

        var asset = await query.SingleOrDefaultAsync(item => item.Id == assetId && item.MediaType == BlobMediaType.Video, cancellationToken);
        if (asset is null)
        {
            return null;
        }

        if (!storageOptions.Enabled)
        {
            var physicalPath = GetLocalMediaPath(asset.BlobName);
            if (!File.Exists(physicalPath))
            {
                return null;
            }

            return new BlobMediaDownload
            {
                Content = File.OpenRead(physicalPath),
                ContentType = asset.ContentType,
                FileName = asset.OriginalFileName
            };
        }

        ValidateConfiguration();
        var download = await GetContainerClient().GetBlobClient(asset.BlobName).DownloadStreamingAsync(cancellationToken: cancellationToken);
        return new BlobMediaDownload
        {
            Content = download.Value.Content,
            ContentType = asset.ContentType,
            FileName = asset.OriginalFileName
        };
    }

    public async Task DeleteImageAsync(
        Guid assetId,
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.BlobMediaAssets
            .Where(asset => asset.Id == assetId && asset.MediaType == BlobMediaType.Image)
            .AsQueryable();

        query = await ApplyVisibilityAsync(query, userId, roles, cancellationToken);
        var asset = await query.SingleOrDefaultAsync(cancellationToken);
        if (asset is null)
        {
            throw new InvalidOperationException("Image introuvable ou non autorisée.");
        }

        var isPrivileged = roles.Any(role => role is Rollen.SuperAdmin or Rollen.ProduktOwner or Rollen.SchulLeiter or Rollen.Koordinator);
        var canDelete = isPrivileged || asset.OwnerId == userId;
        if (!canDelete)
        {
            throw new UnauthorizedAccessException("Vous n’êtes pas autorisé à supprimer cette image.");
        }

        var isInUseByProfile = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.ProfilBildAssetId == assetId, cancellationToken);
        if (isInUseByProfile)
        {
            throw new InvalidOperationException("Suppression impossible : cette image est utilisée comme photo de profil.");
        }

        var mediaRoute = $"/media-library/{assetId}";
        var isInUseByLessonContent = await dbContext.TeacherLessonPages
            .AsNoTracking()
            .AnyAsync(page => page.ContentHtml.Contains(mediaRoute), cancellationToken);
        if (isInUseByLessonContent)
        {
            throw new InvalidOperationException("Suppression impossible : cette image est utilisée dans une leçon.");
        }

        dbContext.BlobMediaAssets.Remove(asset);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!storageOptions.Enabled)
        {
            var physicalPath = GetLocalMediaPath(asset.BlobName);
            if (File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
            }

            return;
        }

        ValidateConfiguration();
        await GetContainerClient().GetBlobClient(asset.BlobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    private async Task<bool> CanStudentOpenImageAsync(Guid assetId, Guid studentId, CancellationToken cancellationToken)
    {
        var profile = await dbContext.StudentProfiles.AsNoTracking()
            .Where(item => item.AppUserId == studentId)
            .Select(item => new { item.SchoolId, item.CurrentKlasseId })
            .FirstOrDefaultAsync(cancellationToken);
        if (profile?.CurrentKlasseId is not Guid klasseId)
        {
            return false;
        }

        var imageUrl = $"/media-library/{assetId:D}";
        var hasImageInLesson = await dbContext.Lessons.AsNoTracking()
            .AnyAsync(lesson => lesson.IsPublished && lesson.KlasseId == klasseId
                && lesson.Klasse.SchoolId == profile.SchoolId
                && (lesson.Content.Contains(imageUrl) || lesson.Exercises.Any(exercise => exercise.Question.Contains(imageUrl))), cancellationToken);
        var hasImageInTeacherLesson = await dbContext.TeacherLessons.AsNoTracking()
            .AnyAsync(lesson => lesson.IsPublished && lesson.KlasseId == klasseId
                && lesson.Klasse.SchoolId == profile.SchoolId
                && lesson.Pages.Any(page => page.ContentHtml.Contains(imageUrl)), cancellationToken);
        var hasImageInExam = await dbContext.Exams.AsNoTracking()
            .AnyAsync(exam => exam.IsPublishedToStudents && exam.KlasseId == klasseId
                && exam.Klasse.SchoolId == profile.SchoolId
                && exam.Questions.Any(question => question.Prompt.Contains(imageUrl)), cancellationToken);

        return (hasImageInLesson || hasImageInTeacherLesson || hasImageInExam)
            && await dbContext.BlobMediaAssets.AsNoTracking().AnyAsync(asset => asset.Id == assetId
                && asset.SchoolId == profile.SchoolId && asset.MediaType == BlobMediaType.Image, cancellationToken);
    }

    private async Task<bool> CanStudentOpenPublishedTeacherLessonVideoAsync(
        Guid assetId,
        Guid studentId,
        Guid? lessonId,
        CancellationToken cancellationToken)
    {
        var profile = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(profile => profile.AppUserId == studentId)
            .Select(profile => new { profile.SchoolId, profile.CurrentKlasseId })
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return false;
        }

        var unlockedKlasseIds = await dbContext.LevelEnrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.StudentId == studentId && enrollment.IstAktuell && enrollment.IsUnlocked)
            .Select(enrollment => enrollment.KlasseId)
            .ToListAsync(cancellationToken);

        var klasseIds = unlockedKlasseIds.Count > 0
            ? unlockedKlasseIds.ToHashSet()
            : profile.CurrentKlasseId is Guid currentKlasseId ? [currentKlasseId] : [];

        return klasseIds.Count > 0
            && await dbContext.TeacherLessons
                .AsNoTracking()
                .AnyAsync(lesson => lesson.IsPublished
                    && (!lessonId.HasValue || lesson.Id == lessonId.Value)
                    && lesson.VideoAssetId == assetId
                    && klasseIds.Contains(lesson.KlasseId)
                    && lesson.AuthorId.HasValue
                    && lesson.Author!.UserSchools.Any(link => link.SchoolId == profile.SchoolId),
                    cancellationToken);
    }

    private async Task<bool> CanStudentOpenPublishedLessonVideoAsync(
        Guid assetId,
        Guid studentId,
        Guid? lessonId,
        CancellationToken cancellationToken)
    {
        var profile = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(profile => profile.AppUserId == studentId)
            .Select(profile => new { profile.SchoolId, profile.CurrentKlasseId })
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return false;
        }

        var unlockedKlasseIds = await dbContext.LevelEnrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.StudentId == studentId && enrollment.IstAktuell && enrollment.IsUnlocked)
            .Select(enrollment => enrollment.KlasseId)
            .ToListAsync(cancellationToken);

        var klasseIds = unlockedKlasseIds.Count > 0
            ? unlockedKlasseIds.ToHashSet()
            : profile.CurrentKlasseId is Guid currentKlasseId ? [currentKlasseId] : [];

        if (klasseIds.Count == 0)
        {
            return false;
        }

        var mediaMarker = $"/media-library/videos/{assetId:D}";
        return await dbContext.Lessons
            .AsNoTracking()
            .AnyAsync(lesson => lesson.IsPublished
                && (!lessonId.HasValue || lesson.Id == lessonId.Value)
                && lesson.Klasse.SchoolId == profile.SchoolId
                && klasseIds.Contains(lesson.KlasseId)
                && lesson.MediaItems.Any(media => media.MediaType == "video" && media.RelativePath.Contains(mediaMarker)),
                cancellationToken);
    }

    private static void ValidateVideo(string contentType, TimeSpan duration)
    {
        if (!AllowedVideoContentTypes.Contains(contentType))
        {
            throw new InvalidOperationException("Seuls les fichiers vidéo MP4 et MOV sont autorisés.");
        }

        if (duration <= TimeSpan.Zero || duration > MaxVideoDuration)
        {
            throw new InvalidOperationException("La vidéo doit durer au maximum 1 heure.");
        }
    }

    private async Task<IQueryable<BlobMediaAsset>> ApplyVisibilityAsync(
        IQueryable<BlobMediaAsset> query,
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        if (roles.Contains(Rollen.SuperAdmin) || roles.Contains(Rollen.ProduktOwner))
        {
            return query.Where(asset => asset.SchoolId != null);
        }

        var schoolIds = await dbContext.UserSchools
            .Where(link => link.AppUserId == userId)
            .Select(link => link.SchoolId)
            .ToListAsync(cancellationToken);

        if (roles.Contains(Rollen.SchulLeiter) || roles.Contains(Rollen.Koordinator))
        {
            return query.Where(asset => asset.SchoolId.HasValue && schoolIds.Contains(asset.SchoolId.Value));
        }

        if (roles.Contains(Rollen.Lehrer))
        {
            return query.Where(asset => asset.SchoolId.HasValue && (asset.OwnerId == userId || schoolIds.Contains(asset.SchoolId.Value)));
        }

        return query.Where(_ => false);
    }

    private async Task<bool> CanUploadToSchoolAsync(Guid userId, IReadOnlyCollection<string> roles, Guid schoolId, CancellationToken cancellationToken)
    {
        if (roles.Contains(Rollen.SuperAdmin) || roles.Contains(Rollen.ProduktOwner))
        {
            return await dbContext.Schools.AnyAsync(school => school.Id == schoolId, cancellationToken);
        }

        return roles.Any(role => role is Rollen.SchulLeiter or Rollen.Koordinator or Rollen.Lehrer)
            && await dbContext.UserSchools.AnyAsync(link => link.AppUserId == userId && link.SchoolId == schoolId, cancellationToken);
    }

    private BlobContainerClient GetContainerClient()
        => new BlobContainerClient(storageOptions.BlobConnectionString, storageOptions.ContainerName);

    private string GetLocalMediaRootPath()
    {
        var configuredRoot = storageOptions.LocalDataRootPath;
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            configuredRoot = DefaultLocalMediaLibraryRoot;
        }

        return Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredRoot));
    }

    private string GetLocalMediaPath(string relativePath)
    {
        var normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(GetLocalMediaRootPath(), normalizedPath);
    }

    private static string BuildLocalMediaPath(Guid schoolId, Guid? klasseId, string originalFileName, string displayName, string mediaFolder)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var safeName = SanitizeBlobSegment(string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(originalFileName) : displayName);
        var classSegment = klasseId.HasValue ? $"classes/{klasseId.Value:N}/" : string.Empty;
        return $"{mediaFolder}/schools/{schoolId:N}/{classSegment}{DateTime.UtcNow:yyyy/MM}/{safeName}-{Guid.NewGuid():N}{extension}";
    }

    private static string BuildBlobStoragePath(Guid schoolId, Guid? klasseId, string originalFileName, string displayName, string mediaFolder)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var safeName = SanitizeBlobSegment(string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(originalFileName) : displayName);
        var classSegment = klasseId.HasValue ? $"classes/{klasseId.Value:N}/" : string.Empty;
        return $"{BlobStorageRootPrefix}/schools/{schoolId:N}/{classSegment}{mediaFolder}/{DateTime.UtcNow:yyyy/MM}/{safeName}-{Guid.NewGuid():N}{extension}";
    }

    private void ValidateConfiguration()
    {
        if (!storageOptions.Enabled || string.IsNullOrWhiteSpace(storageOptions.BlobConnectionString))
        {
            throw new InvalidOperationException("Azure Blob Storage n'est pas configuré. En local : définissez AzureBlobStorage:BlobConnectionString dans les secrets utilisateur et redémarrez l'application. Dans Azure App Service : définissez AzureBlobStorage__BlobConnectionString dans Configuration > Paramètres de l'application, puis redémarrez le service.");
        }
    }

    private static void ValidateImage(string contentType, string altText)
    {
        if (!AllowedContentTypes.Contains(contentType))
        {
            throw new InvalidOperationException("Seuls les fichiers PNG, JPEG, GIF et WebP sont autorisés.");
        }

        if (string.IsNullOrWhiteSpace(altText) || altText.Length > 250)
        {
            throw new InvalidOperationException("Le texte alternatif est obligatoire et ne doit pas dépasser 250 caractères.");
        }
    }

    private static string SanitizeBlobSegment(string value)
    {
        var cleaned = new string(value.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        cleaned = string.Join('-', cleaned.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(cleaned) ? "image" : cleaned;
    }

    private static BlobMediaAssetItem ToItem(BlobMediaAsset asset) => new()
    {
        Id = asset.Id,
        SchoolId = asset.SchoolId,
        SchoolName = asset.School?.Name ?? string.Empty,
        OwnerId = asset.OwnerId,
        OwnerName = asset.Owner.FullName,
        KlasseId = asset.KlasseId,
        KlasseCode = asset.Klasse?.Code ?? string.Empty,
        AltText = asset.AltText,
        DisplayName = asset.DisplayName,
        OriginalFileName = asset.OriginalFileName,
        ContentType = asset.ContentType,
        Length = asset.Length,
        ImageUrl = asset.MediaType == BlobMediaType.Image ? $"/media-library/{asset.Id}" : string.Empty,
        MediaType = asset.MediaType,
        VideoDuration = asset.VideoDuration,
        VideoUrl = asset.MediaType == BlobMediaType.Video ? $"/media-library/videos/{asset.Id}" : string.Empty
    };
}
