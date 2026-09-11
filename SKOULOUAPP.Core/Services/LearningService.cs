using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.Interfaces;
using HalloApp.Core.Models.Student;
using HalloApp.Core.Models.TeacherLessons;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HalloApp.Core.Services;

public class LearningService(IHalloAppDbContext dbContext)
{
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
            .Include(x => x.Exercises)
            .Where(x => x.IsPublished && x.Klasse.SchoolId == profile.SchoolId && accessibleKlasseIds.Contains(x.KlasseId))
            .AsQueryable();

        var lessons = await query
            .OrderBy(x => x.Klasse.Code)
            .ThenBy(x => x.Order)
            .ToListAsync(cancellationToken);

        var lessonIds = lessons.Select(x => x.Id).ToList();

        var progresses = await dbContext.LessonProgresses
            .AsNoTracking()
            .Where(x => x.StudentId == studentId && lessonIds.Contains(x.LessonId))
            .ToListAsync(cancellationToken);

        return lessons
            .Select(lesson =>
            {
                var progress = progresses.FirstOrDefault(p => p.LessonId == lesson.Id);
                return new LessonListItem
                {
                    LessonId = lesson.Id,
                    Title = lesson.Title,
                    Summary = lesson.Summary,
                    Order = lesson.Order,
                    CourseCode = lesson.Klasse.Code,
                    ExerciseCount = lesson.Exercises.Count,
                    CorrectExerciseCount = progress?.CorrectExerciseCount ?? 0,
                    IsCompleted = progress?.IsCompleted ?? false,
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

        var progress = await dbContext.LessonProgresses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.LessonId == lessonId && x.StudentId == studentId, cancellationToken);

        return new LessonDetailViewModel
        {
            LessonId = lesson.Id,
            Title = lesson.Title,
            Summary = lesson.Summary,
            Content = lesson.Content,
            CourseCode = lesson.Klasse.Code,
            IsCompleted = progress?.IsCompleted ?? false,
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
                        ExerciseType = normalizedType,
                        AudioText = exercise.AudioText,
                        TtsLanguage = string.IsNullOrWhiteSpace(exercise.TtsLanguage) ? "de-DE" : exercise.TtsLanguage,
                        GapCount = gapDefinition.Count,
                        HasAutomaticGrading = hasAutomaticGrading,
                        IsPendingManualGrading = hasPendingManual,
                        IsManuallyReviewed = lastManualReview is not null,
                        ManualFeedback = lastManualReview?.ManualFeedback,
                        IsAnsweredCorrectly = lastCorrect is not null,
                        SelectedOptionId = lastCorrect?.SelectedOptionId,
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
            return new AnswerResult { Succeeded = false, Message = "Cette leçon n'appartient pas à ton école." };
        }

        var accessibleKlasseIds = await GetAccessibleKlasseIdsAsync(studentId, cancellationToken);
        if (!accessibleKlasseIds.Contains(exercise.Lesson.KlasseId))
        {
            return new AnswerResult { Succeeded = false, Message = "Ce niveau n'est pas encore débloqué pour ton compte." };
        }

        if (!exercise.Lesson.IsPublished)
        {
            return new AnswerResult { Succeeded = false, Message = "Cette leçon n'est pas encore publiée." };
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
                : $"{correct}/{total} correct(s). Réessaie.";
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
            message = isCorrect ? "Bravo, c'est correct !" : "Ce n'est pas la bonne réponse. Réessaie.";
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
            message = isCorrect ? "Bravo, c'est correct !" : "Ce n'est pas la bonne réponse. Réessaie.";
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
                message = isCorrect ? "Bravo, c'est correct !" : "Ce n'est pas la bonne réponse. Réessaie.";
            }
            else
            {
                // Keine Musterlösung -> manuelle Benotung
                isCorrect = false;
                requiresManualGrading = true;
                awardedPoints = 0;
                message = "Ta réponse a été enregistrée. Elle sera corrigée par ton enseignant.";
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
        => await dbContext.LevelEnrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.StudentId == studentId && enrollment.IsUnlocked)
            .Select(enrollment => enrollment.KlasseId)
            .ToHashSetAsync(cancellationToken);

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
            .Select(x => new { x.Id, LessonVersion = x.Lesson.AssignmentVersion })
            .ToListAsync(cancellationToken);

        var currentVersion = exercises.Select(x => x.LessonVersion).FirstOrDefault();
        var exerciseIds = exercises.Select(x => x.Id).ToList();

        var totalCount = exerciseIds.Count;

        var pendingAttempts = dbContext.ChangeTracker
            .Entries<ExerciseAttempt>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .Where(a => a.StudentId == studentId
                && a.IsCorrect
                && a.AssignmentVersion == currentVersion
                && exerciseIds.Contains(a.ExerciseId))
            .Select(a => a.ExerciseId);

        var persistedCorrect = await dbContext.ExerciseAttempts
            .Where(x => x.StudentId == studentId
                && x.IsCorrect
                && x.AssignmentVersion == currentVersion
                && exerciseIds.Contains(x.ExerciseId))
            .Select(x => x.ExerciseId)
            .ToListAsync(cancellationToken);

        var correctCount = persistedCorrect
            .Concat(pendingAttempts)
            .Distinct()
            .Count();

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
}
