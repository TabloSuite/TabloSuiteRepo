using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.Models.Admin;
using SkoulouApp.Core.Interfaces;
using SkoulouApp.Core.Models.TeacherLessons;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.Core.Services;

/// <summary>
/// Verwaltet die vom Lehrer erstellten Kursskripte (Vorlesungsskripte).
/// Getrennt vom übungsbasierten <see cref="LearningService"/>. Schüler sehen
/// veröffentlichte Skripte für ihre aktuelle und früheren Klassen – unabhängig vom Abo.
/// </summary>
public class TeacherLessonService(ISkoulouAppDbContext dbContext)
{
    /// <summary>Liefert nur Inhalte, die Lehrkräfte bewusst mit ihrem Schulleiter geteilt haben.</summary>
    public async Task<IReadOnlyList<SharedTeacherContentItem>> GetSharedContentForSchoolLeaderAsync(
        Guid schoolLeaderId,
        CancellationToken cancellationToken = default)
    {
        var freieSchulen = new SchulZugangService(dbContext).FreigegebeneSchulen();
        var schoolIds = await dbContext.UserSchools
            .Where(link => link.AppUserId == schoolLeaderId && !link.AppUser.IsBlocked && freieSchulen.Contains(link.SchoolId))
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
                ContentId = lesson.Id,
                SchoolId = lesson.Klasse.SchoolId,
                SchoolName = lesson.Klasse.School != null ? lesson.Klasse.School.Name : string.Empty,
                KlasseId = lesson.KlasseId,
                ContentType = "Support de cours",
                Title = lesson.Title,
                CourseCode = lesson.Klasse.Code,
                AuthorName = lesson.Author!.FullName,
                IstVonDirektionErfasst = lesson.ErfasstVonId != null
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
                ContentId = exam.Id,
                SchoolId = exam.Klasse.SchoolId,
                SchoolName = exam.Klasse.School != null ? exam.Klasse.School.Name : string.Empty,
                KlasseId = exam.KlasseId,
                ContentType = "Examen",
                Title = exam.Title,
                CourseCode = exam.Klasse.Code,
                AuthorName = exam.Author!.FullName,
                IstVonDirektionErfasst = exam.ErfasstVonId != null
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
        var freieSchulen = new SchulZugangService(dbContext).FreigegebeneSchulen();
        var query = dbContext.TeacherLessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.Author)
            .Include(x => x.Pages)
            .Include(x => x.Fach)
            .Include(x => x.VideoAsset)
            .Where(x => x.AuthorId == teacherId && !x.Klasse.IstSystemNiveau
                && x.Klasse.SchoolId.HasValue && freieSchulen.Contains(x.Klasse.SchoolId.Value))
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

        var freieSchulen = new SchulZugangService(dbContext).FreigegebeneSchulen();
        var schoolIds = await dbContext.UserSchools
            .AsNoTracking()
            .Where(link => link.AppUserId == userId && !link.AppUser.IsBlocked && freieSchulen.Contains(link.SchoolId))
            .Select(link => link.SchoolId)
            .ToListAsync(cancellationToken);

        var query = dbContext.TeacherLessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.Author)
            .Include(x => x.Pages)
            .Include(x => x.Fach)
            .Include(x => x.VideoAsset)
            .Where(x => !x.Klasse.IstSystemNiveau && x.Klasse.SchoolId.HasValue && freieSchulen.Contains(x.Klasse.SchoolId.Value)
                && (x.AuthorId == userId
                    || (x.AuthorId.HasValue
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

        var currentKlasseId = profile.CurrentKlasseId;
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
            .Include(x => x.Fach)
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
            item.IsCurrentKlasse = currentKlasseId.HasValue && lesson.KlasseId == currentKlasseId.Value;
            return item;
        }).ToList();
    }

    /// <summary>
    /// Liefert veröffentlichte Lehrerskripte früherer Klassen eines Schülers, auch wenn er aktuell keine Klasse hat.
    /// </summary>
    public async Task<IReadOnlyList<TeacherLessonListItem>> GetHistoryLessonsForStudentAsync(
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

        var klasseIds = await GetStudentHistoryKlasseIdsAsync(studentId, cancellationToken);
        if (profile.CurrentKlasseId is Guid currentKlasseId)
        {
            klasseIds.Remove(currentKlasseId);
        }

        if (klasseIds.Count == 0)
        {
            return [];
        }

        var lessons = await dbContext.TeacherLessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.Author)
            .Include(x => x.Pages)
            .Include(x => x.Fach)
            .Include(x => x.VideoAsset)
            .Where(x => x.IsPublished
                        && klasseIds.Contains(x.KlasseId)
                        && x.Klasse.SchoolId == profile.SchoolId)
            .OrderBy(x => x.Klasse.Code)
            .ThenBy(x => x.Order)
            .ToListAsync(cancellationToken);

        return lessons.Select(lesson =>
        {
            var item = MapToListItem(lesson);
            item.IsPdfExportEnabled = lesson.IsPdfExportEnabled;
            item.IsCurrentKlasse = false;
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

        var klasseIds = await GetStudentHistoryKlasseIdsAsync(studentId, cancellationToken);
        if (klasseIds.Count == 0)
        {
            return null;
        }

        var detail = await GetLessonDetailAsync(lessonId, onlyPublished: true, cancellationToken);
        var belongsToSchool = await dbContext.TeacherLessons
            .AsNoTracking()
            .AnyAsync(x => x.Id == lessonId
                           && klasseIds.Contains(x.KlasseId)
                           && x.Klasse.SchoolId == profile.SchoolId, cancellationToken);
        if (detail is null || !belongsToSchool)
        {
            return null;
        }

        return detail;
    }

    private async Task<HashSet<Guid>> GetStudentHistoryKlasseIdsAsync(
        Guid studentId,
        CancellationToken cancellationToken)
    {
        return await new SchuelerZugangService(dbContext).KlassenAsync(studentId, mitHistorie: true, cancellationToken);
    }

    private async Task<HashSet<Guid>> GetStudentKlasseIdsAsync(
        Guid studentId,
        CancellationToken cancellationToken)
    {
        return await new SchuelerZugangService(dbContext).KlassenAsync(studentId, mitHistorie: true, token: cancellationToken);
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
        if (profile is null)
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

        if (lesson.Klasse.SchoolId is Guid schulID && !await new SchulZugangService(dbContext).IstSchuleFreigegebenAsync(schulID, cancellationToken)) return null;
        var isAuthor = lesson.AuthorId == userId;
        var canViewAsDirection = includeSharedWithDirection
            && lesson.IsSharedWithSchool
            && lesson.IsPdfExportEnabled
            && lesson.Klasse.SchoolId.HasValue
            && await dbContext.UserSchools
                .AsNoTracking()
                .AnyAsync(link => link.AppUserId == userId && link.SchoolId == lesson.Klasse.SchoolId.Value, cancellationToken);

        // Die Direktion, die im Namen des Autors erfassen darf, sieht dessen Skripte
        // wie der Autor selbst (Vorschau/PDF in der Erfassungsansicht).
        var canActForAuthor = !isAuthor && !canViewAsDirection
            && lesson.AuthorId is Guid autorId
            && await new DirektionsErfassungService(dbContext).DarfFuerLehrerHandelnAsync(userId, autorId, cancellationToken);

        if (!isAuthor && !canViewAsDirection && !canActForAuthor)
        {
            return null;
        }

        var detail = MapToDetailViewModel(lesson);
        RemoveVideoFromPdfDetail(detail);
        return detail;
    }

    /// <summary>
    /// Liefert eine reine Leseansicht für Schulleitung/Koordination für alle
    /// Lehrer-Lektionen der eigenen Schulen.
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
            .Include(x => x.Fach)
            .FirstOrDefaultAsync(x => x.Id == lessonId, cancellationToken);
        if (lesson?.Klasse.SchoolId is not Guid schoolId)
        {
            return null;
        }

        var belongsToDirectionSchool = await dbContext.UserSchools
            .AsNoTracking()
            .AnyAsync(link => link.AppUserId == userId && link.SchoolId == schoolId, cancellationToken);
        if (!belongsToDirectionSchool || !await new SchulZugangService(dbContext).IstSchuleFreigegebenAsync(schoolId, cancellationToken))
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

    /// <summary>
    /// Lädt ein Kursskript als bearbeitbares Formularmodell. Mit
    /// <paramref name="handelnderId"/> lädt die Direktion das Skript des Lehrers
    /// <paramref name="authorId"/> (Berechtigung wird geprüft).
    /// </summary>
    public async Task<TeacherLessonFormModel?> GetLessonForEditAsync(
        Guid lessonId,
        Guid authorId,
        CancellationToken cancellationToken = default,
        Guid? handelnderId = null)
    {
        if (handelnderId is Guid handelnder && handelnder != authorId
            && !await new DirektionsErfassungService(dbContext).DarfFuerLehrerHandelnAsync(handelnder, authorId, cancellationToken))
        {
            return null;
        }

        var freieSchulen = new SchulZugangService(dbContext).FreigegebeneSchulen();
        var lesson = await dbContext.TeacherLessons
            .AsNoTracking()
            .Include(x => x.Pages)
            .Include(x => x.Fach)
            .Include(x => x.VideoAsset)
            .FirstOrDefaultAsync(x => x.Id == lessonId && x.AuthorId == authorId && !x.Klasse.IstSystemNiveau
                && x.Klasse.SchoolId.HasValue && freieSchulen.Contains(x.Klasse.SchoolId.Value), cancellationToken);

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
            FachId = lesson.FachId,
            FachName = lesson.Fach?.Name,
            CourseTheme = lesson.CourseTheme,
            IsPublished = lesson.IsPublished,
            IsSharedWithSchool = lesson.IsSharedWithSchool,
            IsPdfExportEnabled = lesson.IsPdfExportEnabled,
            VideoAssetId = lesson.VideoAssetId,
            VideoDisplayName = lesson.VideoAsset?.DisplayName,
            VideoDuration = lesson.VideoAsset?.VideoDuration,
            VideoUrl = lesson.VideoAssetId.HasValue ? $"/media-library/videos/{lesson.VideoAssetId.Value}" : null,
            IstVonDirektionErfasst = lesson.ErfasstVonId.HasValue,
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

    /// <summary>
    /// Erstellt oder aktualisiert ein Kursskript inkl. seiner Seiten. Mit
    /// <paramref name="handelnderId"/> ≠ <paramref name="authorId"/> erfasst die Direktion
    /// im Namen des Lehrers: Berechtigung und Klassenzuordnung des Lehrers werden geprüft,
    /// <see cref="TeacherLesson.ErfasstVonId"/> wird gesetzt. Bearbeitet der Lehrer selbst,
    /// bleibt eine vorhandene Kennzeichnung erhalten.
    /// </summary>
    public async Task<Guid> SaveLessonAsync(
        TeacherLessonFormModel model,
        Guid authorId,
        CancellationToken cancellationToken = default,
        Guid? handelnderId = null)
    {
        var erfasstVonId = await new DirektionsErfassungService(dbContext).PruefeUndErmittleErfasserAsync(handelnderId, authorId, cancellationToken);
        if (erfasstVonId.HasValue)
        {
            await PruefeKlasseDesLehrersAsync(authorId, model.KlasseId, cancellationToken);
        }

        var klasse = await dbContext.Klassen
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == model.KlasseId, cancellationToken)
            ?? throw new InvalidOperationException("Le niveau sélectionné est introuvable.");
        if (klasse.IstSystemNiveau)
        {
            throw new InvalidOperationException("Les niveaux standard font partie du matériel pédagogique et ne peuvent pas être gérés par les enseignants.");
        }

        if (klasse.SchoolId is Guid schulID && !await new SchulZugangService(dbContext).IstSchuleFreigegebenAsync(schulID, cancellationToken))
            throw new UnauthorizedAccessException(SchulZugangService.GesperrtHinweis);

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

        if (erfasstVonId.HasValue)
        {
            lesson.ErfasstVonId = erfasstVonId;
        }

        lesson.KlasseId = model.KlasseId;
        lesson.FachId = await ResolveTeacherFachAsync(authorId, model.KlasseId, model.FachId, model.NeueFachBezeichnung, cancellationToken);
        lesson.Title = model.Title.Trim();
        lesson.Summary = model.Summary?.Trim() ?? string.Empty;
        lesson.CourseTheme = string.IsNullOrWhiteSpace(model.CourseTheme) ? null : model.CourseTheme.Trim();
        if (!model.Id.HasValue)
        {
            var nextOrder = await dbContext.TeacherLessons
                .AsNoTracking()
                .Where(existingLesson => existingLesson.KlasseId == model.KlasseId)
                .MaxAsync(existingLesson => (int?)existingLesson.Order, cancellationToken) ?? 0;
            lesson.Order = nextOrder + 1;
        }
        lesson.IsPublished = model.IsPublished;
        lesson.IsSharedWithSchool = model.IsSharedWithSchool;
        lesson.IsPdfExportEnabled = model.IsPdfExportEnabled;
        lesson.VideoAssetId = model.VideoAssetId;
        lesson.GeaendertAmUtc = DateTime.UtcNow;

        SyncPages(lesson, model.Pages);

        await dbContext.SaveChangesAsync(cancellationToken);
        return lesson.Id;
    }

    public async Task<IReadOnlyList<FachOptionItem>> GetTeacherFachOptionsAsync(
        Guid teacherId,
        CancellationToken cancellationToken = default)
    {
        var schoolIds = await dbContext.UserSchools
            .AsNoTracking()
            .Where(link => link.AppUserId == teacherId)
            .Select(link => link.SchoolId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (schoolIds.Count == 0)
        {
            return [];
        }

        var items = await dbContext.LehrerFachZuordnungen
            .AsNoTracking()
            .Where(assignment => assignment.LehrerId == teacherId && schoolIds.Contains(assignment.Fach.SchoolId))
            .Select(assignment => new FachOptionItem
            {
                FachId = assignment.FachId,
                Name = assignment.Fach.Name,
                SchoolId = assignment.Fach.SchoolId,
                SchoolName = assignment.Fach.School.Name
            })
            .ToListAsync(cancellationToken);

        return items
            .DistinctBy(item => item.FachId)
            .OrderBy(item => item.SchoolName)
            .ThenBy(item => item.Name)
            .ToList();
    }

    /// <summary>
    /// Ermittelt bzw. legt das Fach an und ordnet es dem Lehrer zu. Mit
    /// <paramref name="handelnderId"/> ≠ <paramref name="teacherId"/> handelt die Direktion
    /// (Berechtigung und Klassenzuordnung des Lehrers werden geprüft).
    /// </summary>
    public async Task<Guid?> ResolveTeacherFachAsync(
        Guid teacherId,
        Guid klasseId,
        Guid? fachId,
        string? neueFachBezeichnung,
        CancellationToken cancellationToken,
        Guid? handelnderId = null)
    {
        if (await new DirektionsErfassungService(dbContext).PruefeUndErmittleErfasserAsync(handelnderId, teacherId, cancellationToken) is not null)
        {
            await PruefeKlasseDesLehrersAsync(teacherId, klasseId, cancellationToken);
        }

        var schoolId = await dbContext.Klassen
            .AsNoTracking()
            .Where(klasse => klasse.Id == klasseId)
            .Select(klasse => klasse.SchoolId)
            .FirstOrDefaultAsync(cancellationToken);

        if (schoolId is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(neueFachBezeichnung))
        {
            var normalized = neueFachBezeichnung.Trim();
            if (normalized.Length == 0)
            {
                return null;
            }

            var normalizedKey = normalized.ToUpperInvariant();
            var existing = await dbContext.Faecher
                .FirstOrDefaultAsync(
                    item => item.SchoolId == schoolId.Value && item.Name.ToUpper() == normalizedKey,
                    cancellationToken);

            var selected = existing;
            if (selected is null)
            {
                selected = new Fach
                {
                    SchoolId = schoolId.Value,
                    Name = normalized
                };
                await dbContext.Faecher.AddAsync(selected, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await EnsureTeacherFachAssignmentAsync(teacherId, selected.Id, cancellationToken);
            return selected.Id;
        }

        if (!fachId.HasValue)
        {
            return null;
        }

        var valid = await dbContext.Faecher
            .AsNoTracking()
            .AnyAsync(item => item.Id == fachId.Value && item.SchoolId == schoolId.Value, cancellationToken);
        if (!valid)
        {
            return null;
        }

        await EnsureTeacherFachAssignmentAsync(teacherId, fachId.Value, cancellationToken);
        return fachId.Value;
    }

    private async Task EnsureTeacherFachAssignmentAsync(Guid teacherId, Guid fachId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.LehrerFachZuordnungen
            .AsNoTracking()
            .AnyAsync(assignment => assignment.LehrerId == teacherId && assignment.FachId == fachId, cancellationToken);

        if (exists)
        {
            return;
        }

        await dbContext.LehrerFachZuordnungen.AddAsync(new LehrerFachZuordnung
        {
            LehrerId = teacherId,
            FachId = fachId
        }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Setzt den Veröffentlichungsstatus eines Kursskripts (optional durch die Direktion im Namen des Autors).</summary>
    public async Task SetPublishedAsync(
        Guid lessonId,
        Guid authorId,
        bool isPublished,
        CancellationToken cancellationToken = default,
        Guid? handelnderId = null)
    {
        await new DirektionsErfassungService(dbContext).PruefeUndErmittleErfasserAsync(handelnderId, authorId, cancellationToken);

        var lesson = await dbContext.TeacherLessons
            .Include(x => x.Klasse)
            .FirstOrDefaultAsync(x => x.Id == lessonId && x.AuthorId == authorId, cancellationToken)
            ?? throw new InvalidOperationException("Le cours introuvable.");
        if (lesson.Klasse.IstSystemNiveau)
        {
            throw new InvalidOperationException("Les niveaux standard font partie du matériel pédagogique et ne peuvent pas être gérés par les enseignants.");
        }

        if (lesson.Klasse.SchoolId is Guid schulID && !await new SchulZugangService(dbContext).IstSchuleFreigegebenAsync(schulID, cancellationToken))
            throw new UnauthorizedAccessException(SchulZugangService.GesperrtHinweis);
        lesson.IsPublished = isPublished;
        lesson.GeaendertAmUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Kopiert eine eigene Lehrerlektion als neuen Entwurf in eine andere Klasse
    /// (optional durch die Direktion im Namen des Autors). Die Kopie übernimmt die
    /// Kennzeichnung « Saisi par la direction » der Quelle.
    /// </summary>
    public async Task<Guid> CopyLessonToKlasseAsync(
        Guid lessonId,
        Guid authorId,
        Guid targetKlasseId,
        bool publishDirectly = false,
        CancellationToken cancellationToken = default,
        Guid? handelnderId = null)
    {
        var erfasstVonId = await new DirektionsErfassungService(dbContext).PruefeUndErmittleErfasserAsync(handelnderId, authorId, cancellationToken);
        if (erfasstVonId.HasValue)
        {
            await PruefeKlasseDesLehrersAsync(authorId, targetKlasseId, cancellationToken);
        }

        var source = await dbContext.TeacherLessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.Fach)
            .Include(x => x.Pages)
            .FirstOrDefaultAsync(x => x.Id == lessonId && x.AuthorId == authorId, cancellationToken)
            ?? throw new InvalidOperationException("Le cours source est introuvable.");

        if (source.Klasse.IstSystemNiveau)
        {
            throw new InvalidOperationException("Les niveaux standard font partie du matériel pédagogique et ne peuvent pas être gérés par les enseignants.");
        }

        var targetKlasse = await dbContext.Klassen
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == targetKlasseId, cancellationToken)
            ?? throw new InvalidOperationException("La classe cible est introuvable.");

        if (targetKlasse.IstSystemNiveau)
        {
            throw new InvalidOperationException("La copie vers un niveau standard n'est pas autorisée.");
        }

        if (source.Klasse.SchoolId is not Guid sourceSchool || targetKlasse.SchoolId is not Guid targetSchool
            || !await new SchulZugangService(dbContext).IstSchuleFreigegebenAsync(sourceSchool, cancellationToken)
            || !await new SchulZugangService(dbContext).IstSchuleFreigegebenAsync(targetSchool, cancellationToken))
            throw new UnauthorizedAccessException(SchulZugangService.GesperrtHinweis);

        var nextOrder = await dbContext.TeacherLessons
            .AsNoTracking()
            .Where(x => x.KlasseId == targetKlasseId)
            .Select(x => (int?)x.Order)
            .MaxAsync(cancellationToken) ?? 0;

        var fachId = source.FachId.HasValue && source.Fach is not null && source.Fach.SchoolId == targetKlasse.SchoolId
            ? source.FachId
            : null;

        var copy = new TeacherLesson
        {
            KlasseId = targetKlasseId,
            AuthorId = authorId,
            ErfasstVonId = erfasstVonId ?? source.ErfasstVonId,
            FachId = fachId,
            Title = $"{source.Title} (copie)",
            Summary = source.Summary,
            CourseTheme = source.CourseTheme,
            Order = nextOrder + 1,
            IsPublished = publishDirectly,
            IsSharedWithSchool = source.IsSharedWithSchool,
            IsPdfExportEnabled = source.IsPdfExportEnabled,
            VideoAssetId = null,
            Pages = source.Pages
                .OrderBy(page => page.PageNumber)
                .Select((page, index) => new TeacherLessonPage
                {
                    PageNumber = index + 1,
                    ContentHtml = page.ContentHtml
                })
                .ToList()
        };

        await dbContext.TeacherLessons.AddAsync(copy, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return copy.Id;
    }

    /// <summary>Löscht ein Kursskript inkl. aller Seiten (optional durch die Direktion im Namen des Autors).</summary>
    public async Task DeleteLessonAsync(
        Guid lessonId,
        Guid authorId,
        CancellationToken cancellationToken = default,
        Guid? handelnderId = null)
    {
        await new DirektionsErfassungService(dbContext).PruefeUndErmittleErfasserAsync(handelnderId, authorId, cancellationToken);

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

        if (lesson.Klasse.SchoolId is Guid schulID && !await new SchulZugangService(dbContext).IstSchuleFreigegebenAsync(schulID, cancellationToken))
            throw new UnauthorizedAccessException(SchulZugangService.GesperrtHinweis);
        dbContext.TeacherLessonPages.RemoveRange(lesson.Pages);
        dbContext.TeacherLessons.Remove(lesson);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Erfasst die Direktion im Namen eines Lehrers, darf sie nur Klassen verwenden,
    /// die diesem Lehrer zugeordnet sind.
    /// </summary>
    private async Task PruefeKlasseDesLehrersAsync(Guid lehrerId, Guid klasseId, CancellationToken cancellationToken)
    {
        var zugeordnet = await dbContext.LehrerNiveauZuordnungen
            .AsNoTracking()
            .AnyAsync(link => link.LehrerID == lehrerId && link.KlasseId == klasseId, cancellationToken);
        if (!zugeordnet)
        {
            throw new InvalidOperationException("Cette classe n'est pas attribuée à cet enseignant.");
        }
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
        FachId = lesson.FachId,
        FachName = lesson.Fach?.Name,
        CourseTheme = lesson.CourseTheme,
        CourseCode = lesson.Klasse?.Code ?? string.Empty,
        KlasseId = lesson.KlasseId,
        KlasseTitle = lesson.Klasse?.Title,
        Order = lesson.Order,
        IsPublished = lesson.IsPublished,
        PageCount = lesson.Pages?.Count ?? 0,
        AuthorName = lesson.Author?.FullName ?? string.Empty,
        AuthorId = lesson.AuthorId,
        IsPdfExportEnabled = lesson.IsPdfExportEnabled,
        IsSharedWithSchool = lesson.IsSharedWithSchool,
        HasVideo = lesson.VideoAssetId.HasValue,
        VideoDisplayName = lesson.VideoAsset?.DisplayName,
        IstVonDirektionErfasst = lesson.ErfasstVonId.HasValue
    };

    private static TeacherLessonDetailViewModel MapToDetailViewModel(TeacherLesson lesson) => new()
    {
        Id = lesson.Id,
        Title = lesson.Title,
        Summary = lesson.Summary,
        FachId = lesson.FachId,
        FachName = lesson.Fach?.Name,
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
