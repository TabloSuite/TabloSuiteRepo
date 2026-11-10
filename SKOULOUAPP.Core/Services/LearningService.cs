using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.Interfaces;
using SkoulouApp.Core.Models.Student;
using SkoulouApp.Core.Models.TeacherLessons;
using SkoulouApp.Core.Services.Text;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkoulouApp.Core.Services;

public class LearningService(ISkoulouAppDbContext dbContext)
{
    private const string LessonPageBreakMarker = "<!--SKOULOUAPP_PAGE_BREAK-->";

    private static List<LessonContentPageViewModel> SplitLessonContentPages(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return
            [
                new LessonContentPageViewModel { PageNumber = 1, ContentHtml = "<p><br></p>" }
            ];
        }

        var pages = html
            .Split(LessonPageBreakMarker, StringSplitOptions.None)
            .Select((part, index) => new LessonContentPageViewModel
            {
                PageNumber = index + 1,
                ContentHtml = string.IsNullOrWhiteSpace(part) ? "<p><br></p>" : part
            })
            .ToList();

        return pages.Count == 0
            ? [new LessonContentPageViewModel { PageNumber = 1, ContentHtml = "<p><br></p>" }]
            : pages;
    }

    private static string JoinLessonContentPages(IEnumerable<LessonContentPageViewModel> pages)
        => string.Join(LessonPageBreakMarker, pages.OrderBy(x => x.PageNumber).Select(x => x.ContentHtml ?? string.Empty));
    public async Task<IReadOnlyList<LessonListItem>> GetLessonsForStudentAsync(
        Guid studentId,
        string? levelCode = null,
        CancellationToken cancellationToken = default)
    {
        var profile = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(x => x.AppUserId == studentId)
            .Select(x => new { x.SchoolId })
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return [];
        }

        var accessibleKlasseIds = await GetAccessibleKlasseIdsAsync(studentId, cancellationToken);
        if (accessibleKlasseIds.Count == 0)
        {
            return [];
        }

        var query = dbContext.Lessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.Fach)
            .Include(x => x.CreatedByTeacher)
            .Include(x => x.Exercises)
            .Where(x => x.IsPublished && x.Klasse.SchoolId == profile.SchoolId && accessibleKlasseIds.Contains(x.KlasseId))
            .AsQueryable();

        var lessons = await query
            .OrderBy(x => x.ErstelltAmUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var teacherIds = lessons
            .Where(lesson => lesson.CreatedByTeacherId.HasValue)
            .Select(lesson => lesson.CreatedByTeacherId!.Value)
            .Distinct()
            .ToList();

        var klasseIds = lessons.Where(lesson => !lesson.CreatedByTeacherId.HasValue)
            .Select(lesson => lesson.KlasseId).Distinct().ToList();
        var fachIds = lessons.Where(lesson => !lesson.CreatedByTeacherId.HasValue && lesson.FachId.HasValue)
            .Select(lesson => lesson.FachId!.Value).Distinct().ToList();
        var klasseTeachers = await dbContext.LehrerNiveauZuordnungen.AsNoTracking()
            .Where(link => klasseIds.Contains(link.KlasseId) && link.SchulID == profile.SchoolId)
            .Select(link => new { link.KlasseId, TeacherId = link.LehrerID })
            .ToListAsync(cancellationToken);
        var fachTeachers = await dbContext.LehrerFachZuordnungen.AsNoTracking()
            .Where(link => fachIds.Contains(link.FachId))
            .Select(link => new { link.FachId, TeacherId = link.LehrerId })
            .ToListAsync(cancellationToken);

        teacherIds.AddRange(klasseTeachers.Select(link => link.TeacherId));

        var teacherNames = await dbContext.Users
            .AsNoTracking()
            .Where(user => teacherIds.Contains(user.Id))
            .Select(user => new { user.Id, Name = user.FullName })
            .ToDictionaryAsync(user => user.Id, user => user.Name, cancellationToken);

        var lessonIds = lessons.Select(x => x.Id).ToList();
        var exerciseIds = lessons.SelectMany(lesson => lesson.Exercises).Select(exercise => exercise.Id).ToList();

        var progresses = await dbContext.LessonProgresses
            .AsNoTracking()
            .Where(x => x.StudentId == studentId && lessonIds.Contains(x.LessonId))
            .ToListAsync(cancellationToken);

        var attempts = await dbContext.ExerciseAttempts
            .AsNoTracking()
            .Where(x => x.StudentId == studentId && exerciseIds.Contains(x.ExerciseId))
            .Select(x => new AttemptProgressState
            {
                AttemptId = x.Id,
                ExerciseId = x.ExerciseId,
                AnsweredAtUtc = x.AnsweredAtUtc,
                IsCorrect = x.IsCorrect,
                RequiresManualGrading = x.RequiresManualGrading,
                IsManuallyReviewed = x.ManuallyGradedAtUtc.HasValue,
                AssignmentVersion = x.AssignmentVersion,
                AwardedPoints = x.AwardedPoints
            })
            .ToListAsync(cancellationToken);

        var exerciseMetadata = lessons
            .SelectMany(lesson => lesson.Exercises.Select(exercise => new
            {
                ExerciseId = exercise.Id,
                LessonId = lesson.Id,
                LessonVersion = lesson.AssignmentVersion,
                HasAutomaticGrading = IsChoiceExerciseType(NormalizeExerciseType(exercise.ExerciseType))
                    || NormalizeExerciseType(exercise.ExerciseType) == "GapFill"
                    || AnswerMatchingService.ParseAcceptedAnswers(exercise.AcceptedAnswersJson).Count > 0
            }))
            .ToDictionary(x => x.ExerciseId, x => x);

        var latestAttemptsByExercise = attempts
            .Where(attempt => exerciseMetadata.TryGetValue(attempt.ExerciseId, out var meta)
                && attempt.AssignmentVersion == meta.LessonVersion)
            .GroupBy(attempt => attempt.ExerciseId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(attempt => attempt.AnsweredAtUtc)
                    .ThenByDescending(attempt => attempt.AttemptId)
                    .First());

        var solvedCountsByLesson = lessonIds.ToDictionary(id => id, _ => 0);
        Dictionary<Guid, (int Earned, int Total)> pointsByLesson = lessonIds.ToDictionary(
            id => id,
            _ => (Earned: 0, Total: 0));

        var totalPointsByExerciseId = lessons
            .SelectMany(lesson => lesson.Exercises)
            .ToDictionary(exercise => exercise.Id, exercise => exercise.Points > 0 ? exercise.Points : 10);

        foreach (var lesson in lessons)
        {
            var lessonTotal = lesson.Exercises.Sum(exercise => exercise.Points > 0 ? exercise.Points : 10);
            pointsByLesson[lesson.Id] = (Earned: 0, Total: lessonTotal);
        }

        foreach (var exercise in exerciseMetadata.Values)
        {
            if (!latestAttemptsByExercise.TryGetValue(exercise.ExerciseId, out var latestAttempt))
            {
                continue;
            }

            var maxPoints = totalPointsByExerciseId.TryGetValue(exercise.ExerciseId, out var configuredPoints)
                ? configuredPoints
                : 10;

            var earnedPoints = Math.Clamp(latestAttempt.AwardedPoints, 0, maxPoints);
            var lessonPoints = pointsByLesson[exercise.LessonId];
            pointsByLesson[exercise.LessonId] = (lessonPoints.Earned + earnedPoints, lessonPoints.Total);

            var solved = latestAttempt.IsCorrect
                || (!exercise.HasAutomaticGrading
                    && latestAttempt.IsManuallyReviewed
                    && !latestAttempt.RequiresManualGrading);

            if (solved)
            {
                solvedCountsByLesson[exercise.LessonId]++;
            }
        }

        return lessons
            .Select(lesson =>
            {
                var filterTeacherIds = lesson.CreatedByTeacherId.HasValue
                    ? [lesson.CreatedByTeacherId.Value]
                    : klasseTeachers.Where(link => link.KlasseId == lesson.KlasseId
                            && (!lesson.FachId.HasValue || fachTeachers.Any(fach => fach.FachId == lesson.FachId.Value && fach.TeacherId == link.TeacherId)))
                        .Select(link => link.TeacherId).Distinct().ToList();
                var progress = progresses.FirstOrDefault(p => p.LessonId == lesson.Id);
                var solvedCount = solvedCountsByLesson.TryGetValue(lesson.Id, out var count)
                    ? count
                    : (progress?.CorrectExerciseCount ?? 0);
                (int Earned, int Total) points = pointsByLesson.TryGetValue(lesson.Id, out var lessonPoints)
                    ? lessonPoints
                    : (Earned: 0, Total: lesson.Exercises.Sum(exercise => exercise.Points > 0 ? exercise.Points : 10));
                var isCompleted = lesson.Exercises.Count > 0 && solvedCount >= lesson.Exercises.Count;

                return new LessonListItem
                {
                    LessonId = lesson.Id,
                    Title = lesson.Title,
                    Summary = lesson.Summary,
                    FachName = lesson.Fach != null ? lesson.Fach.Name : string.Empty,
                    TeacherId = lesson.CreatedByTeacherId,
                    TeacherName = lesson.CreatedByTeacherId.HasValue && teacherNames.TryGetValue(lesson.CreatedByTeacherId.Value, out var lessonTeacherName)
                        ? lessonTeacherName
                        : string.Empty,
                    FilterTeacherIds = filterTeacherIds,
                    FilterTeachers = filterTeacherIds.Where(teacherNames.ContainsKey)
                        .Select(id => (id, teacherNames[id])).ToList(),
                    Order = lesson.Order,
                    CourseCode = lesson.Klasse.Code,
                    ExerciseCount = lesson.Exercises.Count,
                    CorrectExerciseCount = solvedCount,
                    ExercisePointsEarned = points.Earned,
                    ExercisePointsTotal = points.Total,
                    ExerciseSubmissionDueAtUtc = lesson.ExerciseSubmissionDueAtUtc,
                    IsCompleted = isCompleted,
                    IsUnlocked = true
                };
            })
            .ToList();
    }

    public async Task<LessonDetailViewModel?> GetLessonDetailAsync(
        Guid lessonId,
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        var lesson = await dbContext.Lessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.MediaItems)
            .Include(x => x.Exercises)
                .ThenInclude(x => x.Options)
            .FirstOrDefaultAsync(x => x.Id == lessonId && x.IsPublished, cancellationToken);

        if (lesson is null)
        {
            return null;
        }

        var profile = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(x => x.AppUserId == studentId)
            .Select(x => new { x.SchoolId })
            .FirstOrDefaultAsync(cancellationToken);
        if (profile is null || lesson.Klasse.SchoolId != profile.SchoolId)
        {
            return null;
        }

        var accessibleKlasseIds = await GetAccessibleKlasseIdsAsync(studentId, cancellationToken);
        if (!accessibleKlasseIds.Contains(lesson.KlasseId))
        {
            return null;
        }

        var exerciseIds = lesson.Exercises.Select(x => x.Id).ToList();

        var attempts = await dbContext.ExerciseAttempts
            .AsNoTracking()
            .Where(x => x.StudentId == studentId
                && x.AssignmentVersion == lesson.AssignmentVersion
                && exerciseIds.Contains(x.ExerciseId))
            .ToListAsync(cancellationToken);

        var isExerciseReviewMode = lesson.ExerciseSubmissionDueAtUtc is DateTime dueAtUtc
            && DateTime.UtcNow > dueAtUtc;

        return new LessonDetailViewModel
        {
            LessonId = lesson.Id,
            Title = lesson.Title,
            Summary = lesson.Summary,
            Content = lesson.Content,
            ContentPages = SplitLessonContentPages(lesson.Content),
            CourseCode = lesson.Klasse.Code,
            IsCompleted = lesson.Exercises.Count > 0 && lesson.Exercises.All(exercise =>
            {
                var latest = attempts.Where(a => a.ExerciseId == exercise.Id)
                    .OrderByDescending(a => a.AnsweredAtUtc)
                    .ThenByDescending(a => a.Id)
                    .FirstOrDefault();
                return latest is not null && (latest.IsCorrect ||
                    (!latest.RequiresManualGrading && latest.ManuallyGradedAtUtc.HasValue));
            }),
            ExerciseSubmissionDueAtUtc = lesson.ExerciseSubmissionDueAtUtc,
            IsExerciseReviewMode = isExerciseReviewMode,
            Media = lesson.MediaItems
                .Select(m => new LessonMediaViewModel
                {
                    MediaType = m.MediaType,
                    Url = NormalizeMediaUrl(m.RelativePath),
                    AltText = m.AltText
                })
                .ToList(),
            Exercises = lesson.Exercises
                .OrderBy(x => x.Order)
                .Select(exercise =>
                {
                    var normalizedType = NormalizeExerciseType(exercise.ExerciseType);
                    var gapDefinition = AnswerMatchingService.ParseGapAnswers(exercise.GapAnswersJson);
                    var acceptedAnswers = AnswerMatchingService.ParseAcceptedAnswers(exercise.AcceptedAnswersJson);

                    var lastCorrect = attempts
                        .Where(a => a.ExerciseId == exercise.Id && a.IsCorrect)
                        .OrderByDescending(a => a.AnsweredAtUtc)
                        .FirstOrDefault();

                    var lastAttempt = attempts
                        .Where(a => a.ExerciseId == exercise.Id)
                        .OrderByDescending(a => a.AnsweredAtUtc)
                        .FirstOrDefault();

                    var hasAutomaticGrading = IsChoiceExerciseType(normalizedType)
                        || normalizedType == "GapFill"
                        || acceptedAnswers.Count > 0;

                    var hasPendingManual = attempts.Any(a => a.ExerciseId == exercise.Id && a.RequiresManualGrading);
                    var lastManualReview = attempts
                        .Where(a => a.ExerciseId == exercise.Id && !a.RequiresManualGrading && a.ManuallyGradedAtUtc.HasValue)
                        .OrderByDescending(a => a.ManuallyGradedAtUtc)
                        .FirstOrDefault();

                    return new ExerciseViewModel
                    {
                        ExerciseId = exercise.Id,
                        Title = exercise.Title,
                        Instructions = exercise.Instructions,
                        Question = exercise.Question,
                        Order = exercise.Order,
                        MaxPoints = exercise.Points,
                        AwardedPoints = lastAttempt?.AwardedPoints,
                        ExerciseType = normalizedType,
                        AudioText = exercise.AudioText,
                        TtsLanguage = string.IsNullOrWhiteSpace(exercise.TtsLanguage) ? "de-DE" : exercise.TtsLanguage,
                        GapCount = gapDefinition.Count,
                        HasAutomaticGrading = hasAutomaticGrading,
                        IsPendingManualGrading = hasPendingManual,
                        IsManuallyReviewed = lastManualReview is not null,
                        ManualIsCorrect = lastManualReview?.ManuallyGradedIsCorrect,
                        ManualFeedback = lastManualReview?.ManualFeedback,
                        AcceptedAnswers = isExerciseReviewMode ? acceptedAnswers.ToList() : [],
                        CorrectGapAnswers = isExerciseReviewMode
                            ? gapDefinition.Select(answers => answers.ToList()).ToList()
                            : [],
                        IsAnsweredCorrectly = lastCorrect is not null,
                        SelectedOptionId = lastAttempt?.SelectedOptionId,
                        SubmittedSelectedOptionIds = ParseGuidIdList(lastAttempt?.SelectedOptionIdsJson),
                        CorrectOptionIds = exercise.Options.Where(option => option.IsCorrect).Select(option => option.Id).ToList(),
                        HasSubmittedAttempt = lastAttempt is not null,
                        SubmittedTextAnswer = lastAttempt?.TextAnswer,
                        SubmittedGapValues = AnswerMatchingService.ParseStudentGapValues(lastAttempt?.GapAnswersJson).ToList(),
                        Options = exercise.Options
                            .OrderBy(o => o.Order)
                            .Select(o => new ExerciseOptionViewModel
                            {
                                OptionId = o.Id,
                                Text = o.Text,
                                Order = o.Order
                            })
                            .ToList()
                    };
                })
                .ToList()
        };
    }

    public async Task<LessonDetailViewModel?> GetLessonDetailForTeacherOrDirectionReadOnlyAsync(
        Guid lessonId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var lesson = await dbContext.Lessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.MediaItems)
            .Include(x => x.Exercises)
                .ThenInclude(x => x.Options)
            .FirstOrDefaultAsync(x => x.Id == lessonId && x.IsPublished, cancellationToken);

        if (lesson is null)
        {
            return null;
        }

        if (lesson.Klasse.SchoolId is not Guid schulID
            || !await new SchulZugangService(dbContext).IstSchuleFreigegebenAsync(schulID, cancellationToken)
            || !await dbContext.Users.AnyAsync(u => u.Id == userId && !u.IsBlocked, cancellationToken)) return null;
        var rollen = await (from ur in dbContext.UserRoles
                            join r in dbContext.Roles on ur.RoleId equals r.Id
                            where ur.UserId == userId select r.Name).ToListAsync(cancellationToken);
        var isDirectionOfSchool = lesson.Klasse.SchoolId.HasValue
            && (rollen.Contains(Rollen.SchulLeiter) || rollen.Contains(Rollen.Koordinator))
            && await dbContext.UserSchools
                .AsNoTracking()
                .AnyAsync(link => link.AppUserId == userId && link.SchoolId == lesson.Klasse.SchoolId.Value, cancellationToken);

        var isAssignedTeacher = rollen.Contains(Rollen.Lehrer) && await dbContext.LehrerNiveauZuordnungen
            .AsNoTracking()
            .AnyAsync(link => link.LehrerID == userId && link.KlasseId == lesson.KlasseId, cancellationToken);

        if (!isDirectionOfSchool && !isAssignedTeacher)
        {
            return null;
        }

        return new LessonDetailViewModel
        {
            LessonId = lesson.Id,
            Title = lesson.Title,
            Summary = lesson.Summary,
            Content = lesson.Content,
            ContentPages = SplitLessonContentPages(lesson.Content),
            CourseCode = lesson.Klasse.Code,
            IsCompleted = false,
            ExerciseSubmissionDueAtUtc = lesson.ExerciseSubmissionDueAtUtc,
            Media = lesson.MediaItems
                .Select(m => new LessonMediaViewModel
                {
                    MediaType = m.MediaType,
                    Url = NormalizeMediaUrl(m.RelativePath),
                    AltText = m.AltText
                })
                .ToList(),
            Exercises = lesson.Exercises
                .OrderBy(x => x.Order)
                .Select(exercise =>
                {
                    var normalizedType = NormalizeExerciseType(exercise.ExerciseType);
                    var gapDefinition = AnswerMatchingService.ParseGapAnswers(exercise.GapAnswersJson);

                    return new ExerciseViewModel
                    {
                        ExerciseId = exercise.Id,
                        Title = exercise.Title,
                        Instructions = exercise.Instructions,
                        Question = exercise.Question,
                        Order = exercise.Order,
                        MaxPoints = exercise.Points,
                        AwardedPoints = null,
                        ExerciseType = normalizedType,
                        AudioText = exercise.AudioText,
                        TtsLanguage = string.IsNullOrWhiteSpace(exercise.TtsLanguage) ? "de-DE" : exercise.TtsLanguage,
                        GapCount = gapDefinition.Count,
                        HasAutomaticGrading = false,
                        IsPendingManualGrading = false,
                        IsManuallyReviewed = false,
                        ManualFeedback = null,
                        IsAnsweredCorrectly = false,
                        SelectedOptionId = null,
                        SubmittedSelectedOptionIds = [],
                        CorrectOptionIds = exercise.Options.Where(option => option.IsCorrect).Select(option => option.Id).ToList(),
                        HasSubmittedAttempt = false,
                        SubmittedTextAnswer = null,
                        SubmittedGapValues = Enumerable.Repeat(string.Empty, gapDefinition.Count).ToList(),
                        Options = exercise.Options
                            .OrderBy(o => o.Order)
                            .Select(o => new ExerciseOptionViewModel
                            {
                                OptionId = o.Id,
                                Text = o.Text,
                                Order = o.Order
                            })
                            .ToList()
                    };
                })
                .ToList()
        };
    }

    public async Task<TeacherLessonDetailViewModel?> GetLessonDetailForDirectionPdfExportAsync(
        Guid lessonId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var lessonDetail = await GetLessonDetailAsync(lessonId, userId, cancellationToken);
        if (lessonDetail is null)
        {
            return null;
        }

        var textParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(lessonDetail.Content))
        {
            textParts.Add(lessonDetail.Content);
        }

        foreach (var exercise in lessonDetail.Exercises.OrderBy(x => x.Order))
        {
            var plainTitle = HtmlSanitizer.ToPlainText(exercise.Title);
            if (!string.IsNullOrWhiteSpace(plainTitle))
            {
                textParts.Add($"<p><strong>{WebUtility.HtmlEncode(plainTitle)}</strong></p>");
            }

            var plainInstructions = HtmlSanitizer.ToPlainText(exercise.Instructions);
            if (!string.IsNullOrWhiteSpace(plainInstructions))
            {
                textParts.Add($"<p>{WebUtility.HtmlEncode(plainInstructions)}</p>");
            }

            var plainQuestion = HtmlSanitizer.ToPlainText(exercise.Question);
            if (!string.IsNullOrWhiteSpace(plainQuestion))
            {
                textParts.Add($"<p>{WebUtility.HtmlEncode(plainQuestion)}</p>");
            }

            var normalizedType = NormalizeExerciseType(exercise.ExerciseType);
            if (normalizedType is "SingleChoice" or "Choice" or "AudioChoice" or "AudioSingleChoice")
            {
                foreach (var option in exercise.Options.OrderBy(option => option.Order))
                {
                    textParts.Add($"<p><span style=\"display:inline-block;width:14px;height:14px;border:1.5px solid #1e293b;border-radius:50%;vertical-align:middle;margin-right:8px;\"></span>{WebUtility.HtmlEncode(HtmlSanitizer.ToPlainText(option.Text))}</p>");
                }
            }
            else if (normalizedType is "MultipleChoice" or "AudioMultipleChoice")
            {
                foreach (var option in exercise.Options.OrderBy(option => option.Order))
                {
                    textParts.Add($"<p><span style=\"display:inline-block;width:14px;height:14px;border:1.5px solid #1e293b;border-radius:2px;vertical-align:middle;margin-right:8px;\"></span>{WebUtility.HtmlEncode(HtmlSanitizer.ToPlainText(option.Text))}</p>");
                }
            }
            else if (normalizedType == "GapFill")
            {
                var gapCount = Math.Max(1, exercise.GapCount);
                textParts.Add($"<p>{string.Join(" ", Enumerable.Repeat("______", gapCount))}</p>");
            }
            else
            {
                textParts.Add("<p>________________________________________</p>");
                textParts.Add("<p>________________________________________</p>");
            }
        }

        return new TeacherLessonDetailViewModel
        {
            Id = lessonDetail.LessonId,
            Title = lessonDetail.Title,
            Summary = lessonDetail.Summary,
            CourseCode = lessonDetail.CourseCode,
            IsPublished = true,
            IsPdfExportEnabled = true,
            Pages =
            [
                new TeacherLessonPageViewModel
                {
                    Id = lessonDetail.LessonId,
                    PageNumber = 1,
                    ContentHtml = string.Join("<hr/>", textParts)
                }
            ]
        };
    }

    public async Task<LessonDetailViewModel?> GetLessonDetailForDirectionReadOnlyAsync(
        Guid lessonId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var lesson = await dbContext.Lessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.MediaItems)
            .Include(x => x.Exercises)
                .ThenInclude(x => x.Options)
            .FirstOrDefaultAsync(x => x.Id == lessonId && x.IsPublished, cancellationToken);

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

        return new LessonDetailViewModel
        {
            LessonId = lesson.Id,
            Title = lesson.Title,
            Summary = lesson.Summary,
            Content = lesson.Content,
            ContentPages = SplitLessonContentPages(lesson.Content),
            CourseCode = lesson.Klasse.Code,
            IsCompleted = false,
            ExerciseSubmissionDueAtUtc = lesson.ExerciseSubmissionDueAtUtc,
            Media = lesson.MediaItems
                .Select(m => new LessonMediaViewModel
                {
                    MediaType = m.MediaType,
                    Url = NormalizeMediaUrl(m.RelativePath),
                    AltText = m.AltText
                })
                .ToList(),
            Exercises = lesson.Exercises
                .OrderBy(x => x.Order)
                .Select(exercise =>
                {
                    var normalizedType = NormalizeExerciseType(exercise.ExerciseType);
                    var gapDefinition = AnswerMatchingService.ParseGapAnswers(exercise.GapAnswersJson);

                    return new ExerciseViewModel
                    {
                        ExerciseId = exercise.Id,
                        Title = exercise.Title,
                        Instructions = exercise.Instructions,
                        Question = exercise.Question,
                        Order = exercise.Order,
                        MaxPoints = exercise.Points,
                        AwardedPoints = null,
                        ExerciseType = normalizedType,
                        AudioText = exercise.AudioText,
                        TtsLanguage = string.IsNullOrWhiteSpace(exercise.TtsLanguage) ? "de-DE" : exercise.TtsLanguage,
                        GapCount = gapDefinition.Count,
                        HasAutomaticGrading = false,
                        IsPendingManualGrading = false,
                        IsManuallyReviewed = false,
                        ManualFeedback = null,
                        IsAnsweredCorrectly = false,
                        SelectedOptionId = null,
                        SubmittedSelectedOptionIds = [],
                        CorrectOptionIds = exercise.Options.Where(option => option.IsCorrect).Select(option => option.Id).ToList(),
                        HasSubmittedAttempt = false,
                        SubmittedTextAnswer = null,
                        SubmittedGapValues = Enumerable.Repeat(string.Empty, gapDefinition.Count).ToList(),
                        Options = exercise.Options
                            .OrderBy(o => o.Order)
                            .Select(o => new ExerciseOptionViewModel
                            {
                                OptionId = o.Id,
                                Text = o.Text,
                                Order = o.Order
                            })
                            .ToList()
                    };
                })
                .ToList()
        };
    }

    private static string NormalizeMediaUrl(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        if (relativePath.StartsWith("http", StringComparison.OrdinalIgnoreCase) || relativePath.StartsWith('/'))
        {
            return relativePath;
        }

        return "/" + relativePath.TrimStart('/');
    }

    public async Task<AnswerResult> SubmitAnswerAsync(
        Guid exerciseId,
        Guid selectedOptionId,
        Guid studentId,
        CancellationToken cancellationToken = default)
        => await SubmitExerciseAsync(
            exerciseId,
            studentId,
            new ExerciseSubmission { SelectedOptionId = selectedOptionId },
            cancellationToken);

    /// <summary>
    /// Generischer Übungs-Submit für Choice-, Text-, Satzbau-, Audio- und Lückentext-Aufgaben.
    /// Automatische Bewertung bei Choice/GapFill/Musterlösung; sonst manuelle Benotung.
    /// </summary>
    public async Task<AnswerResult> SubmitExerciseAsync(
        Guid exerciseId,
        Guid studentId,
        ExerciseSubmission submission,
        CancellationToken cancellationToken = default)
    {
        var exercise = await dbContext.Exercises
            .Include(x => x.Options)
            .Include(x => x.Lesson)
                .ThenInclude(x => x.Klasse)
            .FirstOrDefaultAsync(x => x.Id == exerciseId, cancellationToken);

        if (exercise is null)
        {
            return new AnswerResult { Succeeded = false, Message = "L'exercice est introuvable." };
        }

        var profile = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(x => x.AppUserId == studentId)
            .Select(x => new { x.SchoolId })
            .FirstOrDefaultAsync(cancellationToken);
        if (profile is null || exercise.Lesson.Klasse.SchoolId != profile.SchoolId)
        {
            return new AnswerResult { Succeeded = false, Message = "Cette leçon n'appartient pas à votre école." };
        }

        var accessibleKlasseIds = await GetAccessibleKlasseIdsAsync(studentId, cancellationToken);
        if (!accessibleKlasseIds.Contains(exercise.Lesson.KlasseId))
        {
            return new AnswerResult { Succeeded = false, Message = "Ce niveau n'est pas encore débloqué pour votre compte." };
        }

        if (!exercise.Lesson.IsPublished)
        {
            return new AnswerResult { Succeeded = false, Message = "Cette leçon n'est pas encore publiée." };
        }

        if (exercise.Lesson.ExerciseSubmissionDueAtUtc is DateTime submissionDueAtUtc && DateTime.UtcNow > submissionDueAtUtc)
        {
            return new AnswerResult { Succeeded = false, Message = "La date limite est dépassée pour cet exercice." };
        }

        var normalizedType = NormalizeExerciseType(exercise.ExerciseType);

        bool isCorrect;
        bool requiresManualGrading = false;
        int awardedPoints = 0;
        Guid? correctOptionId = null;
        string? textAnswer = null;
        string? gapValuesJson = null;
        Guid? selectedOptionId = null;
        string? selectedOptionIdsJson = null;
        string message;

        if (normalizedType == "GapFill")
        {
            gapValuesJson = AnswerMatchingService.SerializeStudentGapValues(submission.GapValues ?? []);
            var (correct, total) = AnswerMatchingService.GradeGaps(exercise.GapAnswersJson, gapValuesJson);
            isCorrect = total > 0 && correct == total;
            awardedPoints = isCorrect ? exercise.Points : 0;
            message = isCorrect
                ? "Bravo, toutes les réponses sont correctes !"
                : $"{correct}/{total} correct(s). Réessayez.";
        }
        else if (IsMultipleChoiceExerciseType(normalizedType))
        {
            var selectedIds = (submission.SelectedOptionIds ?? [])
                .Where(id => exercise.Options.Any(o => o.Id == id))
                .Distinct()
                .ToList();
            if (selectedIds.Count == 0)
            {
                return new AnswerResult { Succeeded = false, Message = "Aucune réponse sélectionnée." };
            }

            selectedOptionIdsJson = System.Text.Json.JsonSerializer.Serialize(selectedIds);
            var correctIds = exercise.Options.Where(x => x.IsCorrect).Select(x => x.Id).ToHashSet();
            // Alles-oder-nichts: genau die korrekten Optionen müssen gewählt sein.
            isCorrect = correctIds.Count > 0 && correctIds.SetEquals(selectedIds);
            awardedPoints = isCorrect ? exercise.Points : 0;
            message = isCorrect ? "Bravo, c'est correct !" : "Ce n'est pas la bonne réponse. Réessayez.";
        }
        else if (IsChoiceExerciseType(normalizedType))
        {
            if (submission.SelectedOptionId is null)
            {
                return new AnswerResult { Succeeded = false, Message = "Aucune réponse sélectionnée." };
            }

            var selectedOption = exercise.Options.FirstOrDefault(x => x.Id == submission.SelectedOptionId.Value);
            if (selectedOption is null)
            {
                return new AnswerResult { Succeeded = false, Message = "L'option choisie est invalide." };
            }

            selectedOptionId = selectedOption.Id;
            correctOptionId = exercise.Options.FirstOrDefault(x => x.IsCorrect)?.Id;
            isCorrect = selectedOption.IsCorrect;
            awardedPoints = isCorrect ? exercise.Points : 0;
            message = isCorrect ? "Bravo, c'est correct !" : "Ce n'est pas la bonne réponse. Réessayez.";
        }
        else
        {
            // Text-basierte Typen: FreeText, SentenceBuilding, AudioText
            textAnswer = submission.TextAnswer?.Trim();
            var accepted = AnswerMatchingService.ParseAcceptedAnswers(exercise.AcceptedAnswersJson);
            var dueAtUtc = exercise.Lesson.ExerciseSubmissionDueAtUtc;
            if (accepted.Count == 0 && dueAtUtc.HasValue && DateTime.UtcNow > dueAtUtc.Value)
            {
                return new AnswerResult
                {
                    Succeeded = false,
                    Message = "La date limite est dépassée pour cet exercice ouvert."
                };
            }

            if (accepted.Count > 0)
            {
                isCorrect = AnswerMatchingService.Matches(textAnswer, accepted);
                awardedPoints = isCorrect ? exercise.Points : 0;
                message = isCorrect ? "Bravo, c'est correct !" : "Ce n'est pas la bonne réponse. Réessayez.";
            }
            else
            {
                // Keine Musterlösung -> manuelle Benotung
                isCorrect = false;
                requiresManualGrading = true;
                awardedPoints = 0;
                message = "Votre réponse a été enregistrée. Elle sera corrigée par votre enseignant.";
            }
        }

        var attempt = new ExerciseAttempt
        {
            ExerciseId = exerciseId,
            StudentId = studentId,
            AssignmentVersion = Math.Max(1, exercise.Lesson.AssignmentVersion),
            SelectedOptionId = selectedOptionId,
            SelectedOptionIdsJson = selectedOptionIdsJson,
            TextAnswer = textAnswer,
            GapAnswersJson = gapValuesJson,
            ExerciseSnapshotJson = BuildExerciseSnapshot(exercise),
            IsCorrect = isCorrect,
            RequiresManualGrading = requiresManualGrading,
            AwardedPoints = awardedPoints,
            AnsweredAtUtc = DateTime.UtcNow
        };
        await dbContext.ExerciseAttempts.AddAsync(attempt, cancellationToken);

        var (correctCount, totalCount, lessonCompleted) =
            await UpdateLessonProgressAsync(exercise.LessonId, studentId, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AnswerResult
        {
            Succeeded = true,
            IsCorrect = isCorrect,
            RequiresManualGrading = requiresManualGrading,
            CorrectOptionId = correctOptionId ?? Guid.Empty,
            AwardedPoints = awardedPoints,
            MaxPoints = exercise.Points > 0 ? exercise.Points : 10,
            LessonCompleted = lessonCompleted,
            CorrectExerciseCount = correctCount,
            TotalExerciseCount = totalCount,
            Message = message
        };
    }

    public async Task<IReadOnlyList<PendingManualExerciseReviewItem>> GetPendingManualExerciseReviewsForTeacherAsync(
        Guid teacherId,
        CancellationToken cancellationToken = default)
    {
        var allowedKlasseIds = await dbContext.LehrerNiveauZuordnungen
            .AsNoTracking()
            .Where(x => x.LehrerID == teacherId)
            .Select(x => x.KlasseId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (allowedKlasseIds.Count == 0)
        {
            return [];
        }

        var pending = await dbContext.ExerciseAttempts
            .AsNoTracking()
            .Include(x => x.Exercise)
                .ThenInclude(x => x.Lesson)
                    .ThenInclude(x => x.Klasse)
                        .ThenInclude(x => x.School)
            .Include(x => x.Student)
            .Where(x => x.RequiresManualGrading
                        && x.Exercise.Lesson.IsPublished
                        && x.Exercise.Lesson.ExerciseSubmissionDueAtUtc.HasValue
                        && x.Exercise.Lesson.ExerciseSubmissionDueAtUtc.Value <= DateTime.UtcNow
                        && allowedKlasseIds.Contains(x.Exercise.Lesson.KlasseId))
            .OrderBy(x => x.Exercise.Lesson.Order)
            .ThenBy(x => x.Exercise.Order)
            .ThenBy(x => x.AnsweredAtUtc)
            .ToListAsync(cancellationToken);

        return pending
            .Select(attempt => new PendingManualExerciseReviewItem
            {
                AttemptId = attempt.Id,
                LessonId = attempt.Exercise.LessonId,
                KlasseId = attempt.Exercise.Lesson.KlasseId,
                SchoolId = attempt.Exercise.Lesson.Klasse.SchoolId,
                SchoolName = attempt.Exercise.Lesson.Klasse.School != null ? attempt.Exercise.Lesson.Klasse.School.Name : string.Empty,
                CourseCode = attempt.Exercise.Lesson.Klasse.Code,
                LessonTitle = attempt.Exercise.Lesson.Title,
                ExerciseId = attempt.ExerciseId,
                ExerciseTitle = attempt.Exercise.Title,
                Question = attempt.Exercise.Question,
                ExercisePoints = attempt.Exercise.Points,
                StudentId = attempt.StudentId,
                StudentName = attempt.Student.FullName,
                SubmittedTextAnswer = attempt.TextAnswer,
                SubmittedAtUtc = attempt.AnsweredAtUtc
            })
            .ToList();
    }

    public async Task<(bool Succeeded, string Message)> GradeManualExerciseAttemptAsync(
        Guid teacherId,
        Guid attemptId,
        int awardedPoints,
        string? feedback,
        CancellationToken cancellationToken = default)
    {
        var allowedKlasseIds = await dbContext.LehrerNiveauZuordnungen
            .AsNoTracking()
            .Where(x => x.LehrerID == teacherId)
            .Select(x => x.KlasseId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        var attempt = await dbContext.ExerciseAttempts
            .Include(x => x.Exercise)
                .ThenInclude(x => x.Lesson)
            .FirstOrDefaultAsync(x => x.Id == attemptId, cancellationToken);

        if (attempt is null)
        {
            return (false, "Réponse introuvable.");
        }

        if (!attempt.RequiresManualGrading)
        {
            return (false, "Cette réponse a déjà été corrigée.");
        }

        if (!allowedKlasseIds.Contains(attempt.Exercise.Lesson.KlasseId))
        {
            return (false, "Vous n'êtes pas autorisé à corriger cette réponse.");
        }

        var maxPoints = attempt.Exercise.Points > 0 ? attempt.Exercise.Points : 10;
        if (awardedPoints < 0 || awardedPoints > maxPoints)
        {
            return (false, $"Les points doivent être compris entre 0 et {maxPoints}.");
        }

        var isCorrect = awardedPoints >= maxPoints;
        attempt.IsCorrect = isCorrect;
        attempt.RequiresManualGrading = false;
        attempt.ManuallyGradedIsCorrect = isCorrect;
        attempt.AwardedPoints = awardedPoints;
        attempt.ManualFeedback = string.IsNullOrWhiteSpace(feedback) ? null : feedback.Trim();
        attempt.ManuallyGradedByUserId = teacherId;
        attempt.ManuallyGradedAtUtc = DateTime.UtcNow;
        attempt.GeaendertAmUtc = DateTime.UtcNow;

        await UpdateLessonProgressAsync(attempt.Exercise.LessonId, attempt.StudentId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return (true, "La correction manuelle a été enregistrée.");
    }

    private async Task<HashSet<Guid>> GetAccessibleKlasseIdsAsync(Guid studentId, CancellationToken cancellationToken)
        => await new SchuelerZugangService(dbContext).KlassenAsync(studentId, mitHistorie: true, cancellationToken);

    /// <summary>
    /// Normalisiert den (frei eingegebenen) ExerciseType-String auf einen bekannten Aufgabentyp.
    /// Leerer/unbekannter Wert wird als "Choice" behandelt (Rückwärtskompatibilität).
    /// </summary>
    internal static string NormalizeExerciseType(string? exerciseType)
    {
        if (string.IsNullOrWhiteSpace(exerciseType))
        {
            return "SingleChoice";
        }

        var value = exerciseType.Trim();
        return value.ToLowerInvariant() switch
        {
            "choice" or "singlechoice" => "SingleChoice",
            "multiplechoice" => "MultipleChoice",
            "audiochoice" or "audiosinglechoice" => "AudioSingleChoice",
            "audiomultiplechoice" => "AudioMultipleChoice",
            "gapfill" or "gap" or "lücken" or "luecken" => "GapFill",
            "text" or "freetext" => "Text",
            "sentencebuilding" or "sentence" or "satzbau" => "SentenceBuilding",
            "audiotext" or "audio" => "AudioText",
            _ => "SingleChoice"
        };
    }

    internal static bool IsChoiceExerciseType(string normalizedType)
        => normalizedType is "SingleChoice" or "MultipleChoice" or "AudioSingleChoice" or "AudioMultipleChoice";

    internal static bool IsMultipleChoiceExerciseType(string normalizedType)
        => normalizedType is "MultipleChoice" or "AudioMultipleChoice";

    private async Task<(int CorrectCount, int TotalCount, bool LessonCompleted)> UpdateLessonProgressAsync(
        Guid lessonId,
        Guid studentId,
        CancellationToken cancellationToken)
    {
        var exercises = await dbContext.Exercises
            .Where(x => x.LessonId == lessonId)
            .Select(x => new
            {
                x.Id,
                x.Points,
                x.AcceptedAnswersJson,
                x.ExerciseType,
                LessonVersion = x.Lesson.AssignmentVersion
            })
            .ToListAsync(cancellationToken);

        var currentVersion = exercises.Select(x => x.LessonVersion).FirstOrDefault();
        var exerciseIds = exercises.Select(x => x.Id).ToList();

        var totalCount = exerciseIds.Count;

        var persistedAttempts = await dbContext.ExerciseAttempts
            .Where(x => x.StudentId == studentId
                && x.AssignmentVersion == currentVersion
                && exerciseIds.Contains(x.ExerciseId))
            .Select(x => new AttemptProgressState
            {
                AttemptId = x.Id,
                ExerciseId = x.ExerciseId,
                AnsweredAtUtc = x.AnsweredAtUtc,
                IsCorrect = x.IsCorrect,
                RequiresManualGrading = x.RequiresManualGrading,
                IsManuallyReviewed = x.ManuallyGradedAtUtc.HasValue
            })
            .ToListAsync(cancellationToken);

        var pendingTrackedAttempts = dbContext.ChangeTracker
            .Entries<ExerciseAttempt>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .Where(a => a.StudentId == studentId
                && a.AssignmentVersion == currentVersion
                && exerciseIds.Contains(a.ExerciseId))
            .ToList();

        var allAttempts = persistedAttempts.ToList();

        foreach (var tracked in pendingTrackedAttempts)
        {
            allAttempts.Add(new AttemptProgressState
            {
                AttemptId = tracked.Id,
                ExerciseId = tracked.ExerciseId,
                AnsweredAtUtc = tracked.AnsweredAtUtc,
                IsCorrect = tracked.IsCorrect,
                RequiresManualGrading = tracked.RequiresManualGrading,
                IsManuallyReviewed = tracked.ManuallyGradedAtUtc.HasValue
            });
        }

        var latestByExercise = allAttempts
            .GroupBy(x => x.ExerciseId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(x => x.AnsweredAtUtc)
                    .ThenByDescending(x => x.AttemptId)
                    .First());

        var correctCount = exercises.Count(exercise =>
        {
            if (!latestByExercise.TryGetValue(exercise.Id, out var latestAttempt))
            {
                return false;
            }

            if (latestAttempt.IsCorrect)
            {
                return true;
            }

            var normalizedType = NormalizeExerciseType(exercise.ExerciseType);
            var acceptedAnswers = AnswerMatchingService.ParseAcceptedAnswers(exercise.AcceptedAnswersJson);
            var hasAutomaticGrading = IsChoiceExerciseType(normalizedType)
                || normalizedType == "GapFill"
                || acceptedAnswers.Count > 0;

            return !hasAutomaticGrading
                && latestAttempt.IsManuallyReviewed
                && !latestAttempt.RequiresManualGrading;
        });

        var isCompleted = totalCount > 0 && correctCount >= totalCount;

        var progress = await dbContext.LessonProgresses
            .FirstOrDefaultAsync(x => x.LessonId == lessonId && x.StudentId == studentId, cancellationToken);

        var wasCompleted = progress?.IsCompleted ?? false;

        if (progress is null)
        {
            progress = new LessonProgress
            {
                LessonId = lessonId,
                StudentId = studentId
            };
            await dbContext.LessonProgresses.AddAsync(progress, cancellationToken);
        }

        progress.CorrectExerciseCount = correctCount;
        progress.TotalExerciseCount = totalCount;
        progress.IsCompleted = isCompleted;
        progress.LastActivityUtc = DateTime.UtcNow;
        progress.GeaendertAmUtc = DateTime.UtcNow;
        if (isCompleted && progress.CompletedAtUtc is null)
        {
            progress.CompletedAtUtc = DateTime.UtcNow;
        }

        if (isCompleted && !wasCompleted)
        {
            await SyncCompletedLessonsCountAsync(studentId, cancellationToken);
        }

        return (correctCount, totalCount, isCompleted);
    }

    private sealed class AttemptProgressState
    {
        public Guid AttemptId { get; set; }
        public Guid ExerciseId { get; set; }
        public DateTime AnsweredAtUtc { get; set; }
        public bool IsCorrect { get; set; }
        public bool RequiresManualGrading { get; set; }
        public bool IsManuallyReviewed { get; set; }
        public int AssignmentVersion { get; set; }
        public int AwardedPoints { get; set; }
    }

    private async Task SyncCompletedLessonsCountAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var profile = await dbContext.StudentProfiles
            .FirstOrDefaultAsync(x => x.AppUserId == studentId, cancellationToken);

        if (profile is null)
        {
            return;
        }

        var persistedCompleted = await dbContext.LessonProgresses
            .Where(x => x.StudentId == studentId && x.IsCompleted)
            .Select(x => x.LessonId)
            .ToListAsync(cancellationToken);

        var trackedCompleted = dbContext.ChangeTracker
            .Entries<LessonProgress>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => e.Entity)
            .Where(p => p.StudentId == studentId && p.IsCompleted)
            .Select(p => p.LessonId);

        profile.CompletedLessonsCount = persistedCompleted
            .Concat(trackedCompleted)
            .Distinct()
            .Count();
        profile.GeaendertAmUtc = DateTime.UtcNow;
    }

    private static string BuildExerciseSnapshot(Exercise exercise)
    {
        var snapshot = new
        {
            exercise.Id,
            exercise.Title,
            exercise.Question,
            exercise.ExerciseType,
            exercise.Points,
            Options = exercise.Options
                .OrderBy(option => option.Order)
                .Select(option => new { option.Id, option.Text, option.IsCorrect, option.Order })
                .ToList()
        };

        return JsonSerializer.Serialize(snapshot);
    }

    private static List<Guid> ParseGuidIdList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var direct = JsonSerializer.Deserialize<List<Guid>>(json);
            if (direct is { Count: > 0 })
            {
                return direct;
            }
        }
        catch
        {
        }

        try
        {
            var textValues = JsonSerializer.Deserialize<List<string>>(json);
            if (textValues is null || textValues.Count == 0)
            {
                return [];
            }

            return textValues
                .Select(value => Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty)
                .Where(value => value != Guid.Empty)
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
