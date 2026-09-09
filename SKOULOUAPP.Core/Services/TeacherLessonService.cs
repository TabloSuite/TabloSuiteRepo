using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.Models.Admin;
using HalloApp.Core.Interfaces;
using HalloApp.Core.Models.TeacherLessons;
using Microsoft.EntityFrameworkCore;

namespace HalloApp.Core.Services;

/// <summary>
/// Verwaltet die vom Lehrer erstellten Kursskripte (Vorlesungsskripte).
/// Getrennt vom übungsbasierten <see cref="LearningService"/>. Schüler sehen
/// veröffentlichte Skripte für ihre aktuelle und früheren Klassen – unabhängig vom Abo.
/// </summary>
public class TeacherLessonService(IHalloAppDbContext dbContext)
{
    /// <summary>Liefert nur Inhalte, die Lehrkräfte bewusst mit ihrem Schulleiter geteilt haben.</summary>
    public async Task<IReadOnlyList<SharedTeacherContentItem>> GetSharedContentForSchoolLeaderAsync(
        Guid schoolLeaderId,
        CancellationToken cancellationToken = default)
    {
        var schoolIds = await dbContext.UserSchools
            .Where(link => link.AppUserId == schoolLeaderId)
            .Select(link => link.SchoolId)
            .ToListAsync(cancellationToken);

        var lessons = await dbContext.TeacherLessons
            .AsNoTracking()
            .Where(lesson => lesson.IsSharedWithSchool
                && lesson.AuthorId.HasValue
                && lesson.Klasse.SchoolId.HasValue
                && schoolIds.Contains(lesson.Klasse.SchoolId.Value))
            .Select(lesson => new SharedTeacherContentItem
            {
                SchoolId = lesson.Klasse.SchoolId,
                ContentType = "Support de cours",
                Title = lesson.Title,
                CourseCode = lesson.Klasse.Code,
                AuthorName = lesson.Author!.FullName
            })
            .ToListAsync(cancellationToken);

        var exams = await dbContext.Exams
            .AsNoTracking()
            .Where(exam => exam.IsSharedWithSchool
                && exam.AuthorId.HasValue
                && exam.Klasse.SchoolId.HasValue
                && schoolIds.Contains(exam.Klasse.SchoolId.Value))
            .Select(exam => new SharedTeacherContentItem
            {
                SchoolId = exam.Klasse.SchoolId,
                ContentType = "Examen",
                Title = exam.Title,
                CourseCode = exam.Klasse.Code,
                AuthorName = exam.Author!.FullName
            })
            .ToListAsync(cancellationToken);

        return lessons.Concat(exams)
            .OrderBy(item => item.AuthorName)
            .ThenBy(item => item.ContentType)
            .ThenBy(item => item.Title)
            .ToList();
    }

    /// <summary>Liefert alle Kursskripte für die Lehrer-Verwaltung (inkl. Entwürfe).</summary>
    public async Task<IReadOnlyList<TeacherLessonListItem>> GetLessonsForTeacherAsync(
        Guid teacherId,
        Guid? klasseId = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.TeacherLessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.Author)
            .Include(x => x.Pages)
            .Include(x => x.VideoAsset)
            .Where(x => x.AuthorId == teacherId && !x.Klasse.IstSystemNiveau)
            .AsQueryable();

        if (klasseId.HasValue)
        {
            query = query.Where(x => x.KlasseId == klasseId.Value);
        }

        var lessons = await query
            .OrderBy(x => x.Klasse.Code)
            .ThenBy(x => x.Order)
            .ToListAsync(cancellationToken);

        return lessons.Select(MapToListItem).ToList();
    }

    /// <summary>
    /// Liefert eigene Kursskripte sowie für die Direktion alle veröffentlichten
    /// Kursskripte aus den Schulen des angemeldeten Nutzers.
    /// Fremde Kursskripte sind nur zur Ansicht gedacht; Bearbeitung bleibt dem
    /// Autor vorbehalten.
    /// </summary>
    public async Task<IReadOnlyList<TeacherLessonListItem>> GetLessonsForTeacherOrSchoolDirectionAsync(
        Guid userId,
        bool includeSharedWithDirection,
        Guid? klasseId = null,
        CancellationToken cancellationToken = default)
    {
        if (!includeSharedWithDirection)
        {
            return await GetLessonsForTeacherAsync(userId, klasseId, cancellationToken);
        }

        var schoolIds = await dbContext.UserSchools
            .AsNoTracking()
            .Where(link => link.AppUserId == userId)
            .Select(link => link.SchoolId)
            .ToListAsync(cancellationToken);

        var query = dbContext.TeacherLessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.Author)
            .Include(x => x.Pages)
            .Include(x => x.VideoAsset)
            .Where(x => !x.Klasse.IstSystemNiveau
                && (x.AuthorId == userId
                    || (x.IsPublished
                        && x.Klasse.SchoolId.HasValue
                        && schoolIds.Contains(x.Klasse.SchoolId.Value))))
            .AsQueryable();

        if (klasseId.HasValue)
        {
            query = query.Where(x => x.KlasseId == klasseId.Value);
        }

        var lessons = await query
            .OrderBy(x => x.Klasse.Code)
            .ThenBy(x => x.Order)
            .ThenBy(x => x.Title)
            .ToListAsync(cancellationToken);

        return lessons.Select(MapToListItem).ToList();
    }

    /// <summary>
    /// Liefert für Schüler veröffentlichte, schuleigene Kursskripte für die aktuelle Klasse eines Schülers.
    /// </summary>
    public async Task<IReadOnlyList<TeacherLessonListItem>> GetLessonsForStudentAsync(
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        var profile = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(x => x.AppUserId == studentId)
            .Select(x => new { x.SchoolId, x.CurrentKlasseId })
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return [];
        }

        var klasseIds = await GetStudentKlasseIdsAsync(studentId, cancellationToken);
        if (klasseIds.Count == 0)
        {
            return [];
        }

        var lessons = await dbContext.TeacherLessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.Author)
            .Include(x => x.Pages)
            .Include(x => x.VideoAsset)
            .Where(x => x.IsPublished
                        && klasseIds.Contains(x.KlasseId)
                        && x.AuthorId.HasValue
                        && x.Author!.UserSchools.Any(link => link.SchoolId == profile.SchoolId))
            .OrderBy(x => x.Klasse.Code)
            .ThenBy(x => x.Order)
            .ToListAsync(cancellationToken);

        return lessons.Select(lesson =>
        {
            var item = MapToListItem(lesson);
            item.IsPdfExportEnabled = lesson.IsPdfExportEnabled;
            return item;
        }).ToList();
    }

    /// <summary>Liefert ein Kursskript inkl. Seiten für den Reader/die Vorschau.</summary>
    public async Task<TeacherLessonDetailViewModel?> GetLessonDetailAsync(
        Guid lessonId,
        bool onlyPublished,
        CancellationToken cancellationToken = default)
    {
        var lesson = await dbContext.TeacherLessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.Pages)
            .Include(x => x.VideoAsset)
            .FirstOrDefaultAsync(x => x.Id == lessonId, cancellationToken);

        if (lesson is null || (onlyPublished && !lesson.IsPublished))
        {
            return null;
        }

        return MapToDetailViewModel(lesson);
    }

    /// <summary>Prüft die Zugriffsberechtigung eines Schülers auf ein Kursskript.</summary>
    public async Task<TeacherLessonDetailViewModel?> GetLessonDetailForStudentAsync(
        Guid lessonId,
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        var profile = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(x => x.AppUserId == studentId)
            .Select(x => new { x.SchoolId, x.CurrentKlasseId })
            .FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var klasseIds = await GetStudentKlasseIdsAsync(studentId, cancellationToken);
        if (klasseIds.Count == 0)
        {
            return null;
        }

        var detail = await GetLessonDetailAsync(lessonId, onlyPublished: true, cancellationToken);
        var belongsToSchool = await dbContext.TeacherLessons
            .AsNoTracking()
            .AnyAsync(x => x.Id == lessonId
                           && klasseIds.Contains(x.KlasseId)
                           && x.AuthorId.HasValue
                           && x.Author!.UserSchools.Any(link => link.SchoolId == profile.SchoolId), cancellationToken);
        if (detail is null || !belongsToSchool)
        {
            return null;
        }

        return detail;
    }

    private async Task<HashSet<Guid>> GetStudentKlasseIdsAsync(
        Guid studentId,
        CancellationToken cancellationToken)
    {
        return await dbContext.LevelEnrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.StudentId == studentId && enrollment.IsUnlocked)
            .Select(enrollment => enrollment.KlasseId)
            .ToHashSetAsync(cancellationToken);
    }

    /// <summary>
    /// Liefert ein vom Autor für PDF freigegebenes Lehrerskript für einen Schüler.
    /// </summary>
    public async Task<TeacherLessonDetailViewModel?> GetLessonDetailForStudentPdfExportAsync(
        Guid lessonId,
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        var profile = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(profile => profile.AppUserId == studentId)
            .Select(profile => new { profile.SchoolId, profile.CurrentKlasseId })
            .FirstOrDefaultAsync(cancellationToken);
        if (profile?.CurrentKlasseId is not Guid klasseId)
        {
            return null;
        }

        var isExportEnabled = await dbContext.TeacherLessons
            .AsNoTracking()
            .AnyAsync(lesson => lesson.Id == lessonId && lesson.IsPdfExportEnabled, cancellationToken);
        if (!isExportEnabled)
        {
            return null;
        }

        var detail = await GetLessonDetailForStudentAsync(lessonId, studentId, cancellationToken);
        RemoveVideoFromPdfDetail(detail);
        return detail;
    }

    /// <summary>
    /// Liefert ein Skript für den PDF-Export für dessen Ersteller oder für eine
    /// berechtigte Schulleitung/Koordination, wenn der Autor PDF-Export und
    /// Direktion-Sichtbarkeit freigegeben hat.
    /// </summary>
    public async Task<TeacherLessonDetailViewModel?> GetLessonDetailForPdfExportAsync(
        Guid lessonId,
        Guid userId,
        bool includeSharedWithDirection = false,
        CancellationToken cancellationToken = default)
    {
        var lesson = await dbContext.TeacherLessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.Pages)
            .FirstOrDefaultAsync(x => x.Id == lessonId, cancellationToken);
        if (lesson is null)
        {
            return null;
        }

        var isAuthor = lesson.AuthorId == userId;
        var canViewAsDirection = includeSharedWithDirection
            && lesson.IsSharedWithSchool
            && lesson.IsPdfExportEnabled
            && lesson.Klasse.SchoolId.HasValue
            && await dbContext.UserSchools
                .AsNoTracking()
                .AnyAsync(link => link.AppUserId == userId && link.SchoolId == lesson.Klasse.SchoolId.Value, cancellationToken);

        if (!isAuthor && !canViewAsDirection)
        {
            return null;
        }

        var detail = MapToDetailViewModel(lesson);
        RemoveVideoFromPdfDetail(detail);
        return detail;
    }

    /// <summary>
    /// Liefert eine reine Leseansicht für Schulleitung/Koordination, wenn der
    /// Lehrer den Inhalt explizit für die Direktion freigegeben hat.
    /// </summary>
    public async Task<TeacherLessonDetailViewModel?> GetLessonDetailForDirectionAsync(
        Guid lessonId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var lesson = await dbContext.TeacherLessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.Pages)
            .FirstOrDefaultAsync(x => x.Id == lessonId && x.IsSharedWithSchool, cancellationToken);
        if (lesson?.Klasse.SchoolId is not Guid schoolId)
        {
            return null;
        }

        var belongsToDirectionSchool = await dbContext.UserSchools
            .AsNoTracking()
            .AnyAsync(link => link.AppUserId == userId && link.SchoolId == schoolId, cancellationToken);
        if (!belongsToDirectionSchool)
        {
            return null;
        }

        return MapToDetailViewModel(lesson);
    }

    private static void RemoveVideoFromPdfDetail(TeacherLessonDetailViewModel? detail)
    {
        if (detail is null)
        {
            return;
        }

        detail.VideoAssetId = null;
        detail.VideoDisplayName = null;
        detail.VideoDuration = null;
        detail.VideoUrl = null;
    }

    /// <summary>Lädt ein Kursskript als bearbeitbares Formularmodell.</summary>
    public async Task<TeacherLessonFormModel?> GetLessonForEditAsync(
        Guid lessonId,
        Guid authorId,
        CancellationToken cancellationToken = default)
    {
        var lesson = await dbContext.TeacherLessons
            .AsNoTracking()
            .Include(x => x.Pages)
            .Include(x => x.VideoAsset)
            .FirstOrDefaultAsync(x => x.Id == lessonId && x.AuthorId == authorId && !x.Klasse.IstSystemNiveau, cancellationToken);

        if (lesson is null)
        {
            return null;
        }

        return new TeacherLessonFormModel
        {
            Id = lesson.Id,
            KlasseId = lesson.KlasseId,
            Title = lesson.Title,
            Summary = lesson.Summary,
            CourseTheme = lesson.CourseTheme,
            Order = lesson.Order,
            IsPublished = lesson.IsPublished,
            IsSharedWithSchool = lesson.IsSharedWithSchool,
            IsPdfExportEnabled = lesson.IsPdfExportEnabled,
            VideoAssetId = lesson.VideoAssetId,
            VideoDisplayName = lesson.VideoAsset?.DisplayName,
            VideoDuration = lesson.VideoAsset?.VideoDuration,
            VideoUrl = lesson.VideoAssetId.HasValue ? $"/media-library/videos/{lesson.VideoAssetId.Value}" : null,
            Pages = lesson.Pages
                .OrderBy(p => p.PageNumber)
                .Select(p => new TeacherLessonPageFormModel
                {
                    Id = p.Id,
                    PageNumber = p.PageNumber,
                    ContentHtml = p.ContentHtml
                })
                .ToList()
        };
    }

    /// <summary>Erstellt oder aktualisiert ein Kursskript inkl. seiner Seiten.</summary>
    public async Task<Guid> SaveLessonAsync(
        TeacherLessonFormModel model,
        Guid authorId,
        CancellationToken cancellationToken = default)
    {
        var klasse = await dbContext.Klassen
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == model.KlasseId, cancellationToken)
            ?? throw new InvalidOperationException("Le niveau sélectionné est introuvable.");
        if (klasse.IstSystemNiveau)
        {
            throw new InvalidOperationException("Les niveaux standard font partie du matériel pédagogique et ne peuvent pas être gérés par les enseignants.");
        }

        TeacherLesson lesson;

        if (model.Id.HasValue)
        {
            lesson = await dbContext.TeacherLessons
                .Include(x => x.Klasse)
                .Include(x => x.Pages)
            .Include(x => x.VideoAsset)
                .FirstOrDefaultAsync(x => x.Id == model.Id.Value && x.AuthorId == authorId && !x.Klasse.IstSystemNiveau, cancellationToken)
                ?? throw new InvalidOperationException("Le cours introuvable.");
        }
        else
        {
            lesson = new TeacherLesson
            {
                AuthorId = authorId
            };
            await dbContext.TeacherLessons.AddAsync(lesson, cancellationToken);
        }

        lesson.KlasseId = model.KlasseId;
        lesson.Title = model.Title.Trim();
        lesson.Summary = model.Summary?.Trim() ?? string.Empty;
        lesson.CourseTheme = string.IsNullOrWhiteSpace(model.CourseTheme) ? null : model.CourseTheme.Trim();
        lesson.Order = model.Order;
        lesson.IsPublished = model.IsPublished;
        lesson.IsSharedWithSchool = model.IsSharedWithSchool;
        lesson.IsPdfExportEnabled = model.IsPdfExportEnabled;
        lesson.VideoAssetId = model.VideoAssetId;
        lesson.GeaendertAmUtc = DateTime.UtcNow;

        SyncPages(lesson, model.Pages);

        await dbContext.SaveChangesAsync(cancellationToken);
        return lesson.Id;
    }

    /// <summary>Setzt den Veröffentlichungsstatus eines Kursskripts.</summary>
    public async Task SetPublishedAsync(
        Guid lessonId,
        Guid authorId,
        bool isPublished,
        CancellationToken cancellationToken = default)
    {
        var lesson = await dbContext.TeacherLessons
            .Include(x => x.Klasse)
            .FirstOrDefaultAsync(x => x.Id == lessonId && x.AuthorId == authorId, cancellationToken)
            ?? throw new InvalidOperationException("Le cours introuvable.");
        if (lesson.Klasse.IstSystemNiveau)
        {
            throw new InvalidOperationException("Les niveaux standard font partie du matériel pédagogique et ne peuvent pas être gérés par les enseignants.");
        }

        lesson.IsPublished = isPublished;
        lesson.GeaendertAmUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Löscht ein Kursskript inkl. aller Seiten.</summary>
    public async Task DeleteLessonAsync(
        Guid lessonId,
        Guid authorId,
        CancellationToken cancellationToken = default)
    {
        var lesson = await dbContext.TeacherLessons
            .Include(x => x.Klasse)
            .Include(x => x.Pages)
            .Include(x => x.VideoAsset)
            .FirstOrDefaultAsync(x => x.Id == lessonId && x.AuthorId == authorId, cancellationToken);

        if (lesson is null)
        {
            return;
        }
        if (lesson.Klasse.IstSystemNiveau)
        {
            throw new InvalidOperationException("Les niveaux standard font partie du matériel pédagogique et ne peuvent pas être gérés par les enseignants.");
        }

        dbContext.TeacherLessonPages.RemoveRange(lesson.Pages);
        dbContext.TeacherLessons.Remove(lesson);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void SyncPages(TeacherLesson lesson, List<TeacherLessonPageFormModel> pages)
    {
        var orderedPages = pages
            .Select((p, index) => (Form: p, Number: index + 1))
            .ToList();

        var keepIds = orderedPages
            .Where(x => x.Form.Id.HasValue)
            .Select(x => x.Form.Id!.Value)
            .ToHashSet();

        var toRemove = lesson.Pages.Where(p => !keepIds.Contains(p.Id)).ToList();
        foreach (var page in toRemove)
        {
            lesson.Pages.Remove(page);
        }

        foreach (var (form, number) in orderedPages)
        {
            var existing = form.Id.HasValue
                ? lesson.Pages.FirstOrDefault(p => p.Id == form.Id.Value)
                : null;

            if (existing is null)
            {
                lesson.Pages.Add(new TeacherLessonPage
                {
                    PageNumber = number,
                    ContentHtml = form.ContentHtml ?? string.Empty
                });
            }
            else
            {
                existing.PageNumber = number;
                existing.ContentHtml = form.ContentHtml ?? string.Empty;
                existing.GeaendertAmUtc = DateTime.UtcNow;
            }
        }
    }

    private static TeacherLessonListItem MapToListItem(TeacherLesson lesson) => new()
    {
        Id = lesson.Id,
        Title = lesson.Title,
        Summary = lesson.Summary,
        CourseTheme = lesson.CourseTheme,
        CourseCode = lesson.Klasse?.Code ?? string.Empty,
        KlasseId = lesson.KlasseId,
        Order = lesson.Order,
        IsPublished = lesson.IsPublished,
        PageCount = lesson.Pages?.Count ?? 0,
        AuthorName = lesson.Author?.FullName ?? string.Empty,
        AuthorId = lesson.AuthorId,
        IsPdfExportEnabled = lesson.IsPdfExportEnabled,
        IsSharedWithSchool = lesson.IsSharedWithSchool,
        HasVideo = lesson.VideoAssetId.HasValue,
        VideoDisplayName = lesson.VideoAsset?.DisplayName
    };

    private static TeacherLessonDetailViewModel MapToDetailViewModel(TeacherLesson lesson) => new()
    {
        Id = lesson.Id,
        Title = lesson.Title,
        Summary = lesson.Summary,
        CourseTheme = lesson.CourseTheme,
        CourseCode = lesson.Klasse.Code,
        IsPublished = lesson.IsPublished,
        IsPdfExportEnabled = lesson.IsPdfExportEnabled,
        VideoAssetId = lesson.VideoAssetId,
        VideoDisplayName = lesson.VideoAsset?.DisplayName,
        VideoDuration = lesson.VideoAsset?.VideoDuration,
        VideoUrl = lesson.VideoAssetId.HasValue ? $"/media-library/videos/{lesson.VideoAssetId.Value}?lessonId={lesson.Id}" : null,
        Pages = lesson.Pages
            .OrderBy(p => p.PageNumber)
            .Select(p => new TeacherLessonPageViewModel
            {
                Id = p.Id,
                PageNumber = p.PageNumber,
                ContentHtml = p.ContentHtml
            })
            .ToList()
    };

    private Task<bool> IsSchoolLeaderAsync(Guid schoolId, Guid userId, CancellationToken cancellationToken)
        => dbContext.UserSchools.AnyAsync(x => x.AppUserId == userId && x.SchoolId == schoolId, cancellationToken);
}
