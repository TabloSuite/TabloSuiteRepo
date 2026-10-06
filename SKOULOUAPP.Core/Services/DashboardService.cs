using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Interfaces;
using SkoulouApp.Core.Models.Admin;
using SkoulouApp.Core.Models.Dashboard;
using SkoulouApp.Core.Models.Home;
using SkoulouApp.Core.Models.Student;
using SkoulouApp.Core.Models.TeacherLessons;
using SkoulouApp.Core.Services.Storage;
using SkoulouApp.Core.Services.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace SkoulouApp.Core.Services;

public class DashboardService(
    ISkoulouAppDbContext dbContext,
    UserManager<AppUser> userManager,
    IMediaStorageService mediaStorageService,
    IHostEnvironment? hostEnvironment = null)
{
    public async Task<IReadOnlyList<Guid>> GetPlatformSchoolIdsAsync(Guid actorId, CancellationToken cancellationToken = default)
    {
        var actor = await userManager.FindByIdAsync(actorId.ToString());
        if (actor is null) return [];
        if (await userManager.IsInRoleAsync(actor, Rollen.SuperAdmin))
            return await dbContext.Schools.AsNoTracking().Select(school => school.Id).ToListAsync(cancellationToken);
        if (!await userManager.IsInRoleAsync(actor, Rollen.ProduktOwner)) return [];
        return await dbContext.UserSchools.AsNoTracking()
            .Where(link => link.AppUserId == actorId)
            .Select(link => link.SchoolId).ToListAsync(cancellationToken);
    }

    public async Task<DashboardSummary> GetPlatformSummaryAsync(Guid actorId, CancellationToken cancellationToken = default)
    {
        var actor = await userManager.FindByIdAsync(actorId.ToString());
        if (actor is null) return new DashboardSummary();
        if (await userManager.IsInRoleAsync(actor, Rollen.SuperAdmin)) return await GetAdminSummaryAsync(cancellationToken);
        var schools = await GetPlatformSchoolIdsAsync(actorId, cancellationToken);
        var users = await GetPlatformUsersAsync(actorId, cancellationToken);
        return new DashboardSummary
        {
            SchoolCount = schools.Count,
            ActiveSchoolCount = await dbContext.Schools.CountAsync(school => schools.Contains(school.Id) && school.IsActive, cancellationToken),
            TeacherCount = users.Count(user => user.RoleName == Rollen.Lehrer),
            StudentCount = users.Count(user => user.RoleName == Rollen.Schueler),
            BlockedStudentCount = users.Count(user => user.RoleName == Rollen.Schueler && user.IsBlocked),
            BlockedAccountCount = users.Count(user => user.IsBlocked),
            CourseCount = await dbContext.Klassen.CountAsync(klasse => klasse.SchoolId.HasValue && schools.Contains(klasse.SchoolId.Value), cancellationToken)
        };
    }

    public async Task<IReadOnlyList<SchoolManagementViewModel>> GetPlatformSchoolsAsync(Guid actorId, CancellationToken cancellationToken = default)
    {
        var allowed = await GetPlatformSchoolIdsAsync(actorId, cancellationToken);
        return (await GetSchoolManagementAsync(cancellationToken))
            .Where(school => allowed.Contains(school.SchoolId)).ToList();
    }

    public async Task<IReadOnlyList<UserManagementItem>> GetPlatformUsersAsync(Guid actorId, CancellationToken cancellationToken = default)
    {
        var actor = await userManager.FindByIdAsync(actorId.ToString());
        if (actor is null) return [];
        var users = await GetUserManagementAsync(cancellationToken);
        if (await userManager.IsInRoleAsync(actor, Rollen.SuperAdmin)) return users;
        if (!await userManager.IsInRoleAsync(actor, Rollen.ProduktOwner)) return [];
        var schoolIds = await GetPlatformSchoolIdsAsync(actorId, cancellationToken);
        var allowedIds = await dbContext.UserSchools.AsNoTracking()
            .Where(link => schoolIds.Contains(link.SchoolId))
            .Select(link => link.AppUserId).Distinct().ToListAsync(cancellationToken);
        var schoolNames = await dbContext.Schools.AsNoTracking()
            .Where(school => schoolIds.Contains(school.Id))
            .Select(school => school.Name).ToListAsync(cancellationToken);
        var visibleUsers = users.Where(user => allowedIds.Contains(user.UserId) && user.RoleName != Rollen.SuperAdmin
            && user.RoleName != Rollen.ProduktOwner).ToList();
        foreach (var user in visibleUsers)
        {
            user.Schools = user.Schools.Where(school => schoolNames.Contains(school)).ToList();
            if (user.RoleName == Rollen.Schueler)
            {
                user.HasPaidEnrollment = false;
                user.IsSkoulouPlus = false;
                user.SchoolFeeAmount = null;
                user.SchoolFeePaidAmount = 0m;
                user.HasStudentClass = false;
                user.HasActiveSchoolFeeCycle = false;
                user.CombinedScorePercent = null;
                user.CombinedScoreCount = 0;
            }
        }
        return visibleUsers;
    }

    public async Task<(bool Succeeded, string Message)> CreatePlatformUserAsync(Guid actorId, UserFormModel model, CancellationToken cancellationToken = default)
    {
        var actor = await userManager.FindByIdAsync(actorId.ToString());
        if (actor is null) return (false, "Accès refusé.");
        var admin = await userManager.IsInRoleAsync(actor, Rollen.SuperAdmin);
        var owner = !admin && await userManager.IsInRoleAsync(actor, Rollen.ProduktOwner);
        if (model.UserId.HasValue || !(admin && model.RoleName is Rollen.SuperAdmin or Rollen.ProduktOwner or Rollen.SchulLeiter
            || owner && model.RoleName == Rollen.SchulLeiter))
            return (false, "Ce rôle ne peut pas être créé ici.");

        model.SchoolIds = model.SchoolIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (model.RoleName == Rollen.SuperAdmin && model.SchoolIds.Count > 0
            || model.RoleName is Rollen.SchulLeiter or Rollen.ProduktOwner && model.SchoolIds.Count == 0)
            return (false, "Veuillez attribuer au moins une école au directeur ou au responsable produit.");
        var allowed = await GetPlatformSchoolIdsAsync(actorId, cancellationToken);
        if (model.SchoolIds.Any(id => !allowed.Contains(id)))
            return (false, "Vous ne pouvez attribuer que vos écoles.");

        model.IsBlocked = false;
        model.IsSelfRegistered = false;
        model.TeacherClassAssignments.Clear();
        model.FachIds.Clear();
        return await SaveUserAsync(model, cancellationToken);
    }

    public async Task<(bool Succeeded, string Message)> SetPlatformUserBlockedAsync(Guid actorId, Guid targetId, bool blocked, CancellationToken cancellationToken = default)
    {
        var actor = await userManager.FindByIdAsync(actorId.ToString());
        var target = await userManager.FindByIdAsync(targetId.ToString());
        if (actor is null || target is null || actorId == targetId)
            return (false, "Accès refusé.");
        var admin = await userManager.IsInRoleAsync(actor, Rollen.SuperAdmin);
        if (!admin && !await userManager.IsInRoleAsync(actor, Rollen.ProduktOwner))
            return (false, "Accès refusé.");
        if (!admin)
        {
            var schoolIds = await GetPlatformSchoolIdsAsync(actorId, cancellationToken);
            if (!await dbContext.UserSchools.AsNoTracking().AnyAsync(link => link.AppUserId == targetId && schoolIds.Contains(link.SchoolId), cancellationToken))
                return (false, "Cet utilisateur n'appartient pas à vos écoles.");
            var roles = await userManager.GetRolesAsync(target);
            if (roles.Contains(Rollen.SuperAdmin) || roles.Contains(Rollen.ProduktOwner))
                return (false, "Ce compte ne peut pas être bloqué ici.");
        }
        var changed = await SetStudentBlockedStateAsync(targetId, blocked, allowBlockingPaidStudent: admin, cancellationToken: cancellationToken);
        return changed
            ? (true, blocked ? "L'utilisateur a été bloqué." : "L'utilisateur a été activé.")
            : (false, "Un élève ayant un abonnement payé ne peut pas être bloqué ici.");
    }

    // Zentrale Regel fuer die erlaubten Niveaus eines Schuelers. Optional, damit
    // bestehende Konstruktoraufrufe (u. a. Tests) unveraendert funktionieren.
    private readonly NiveauGruppenService _niveauGruppen = new(dbContext);
    private async Task<decimal> GetGlobalSubscriptionPriceAsync(CancellationToken cancellationToken)
        => await dbContext.CommissionSettings
            .AsNoTracking()
            .OrderBy(x => x.ErstelltAmUtc)
            .Select(x => (decimal?)x.GlobalSubscriptionPrice)
            .FirstOrDefaultAsync(cancellationToken)
        ?? SkoulouAppKonstanten.StandardPreisAbo;
    public async Task<DashboardSummary> GetAdminSummaryAsync(CancellationToken cancellationToken = default)
    {
        var teacherIds = await userManager.GetUsersInRoleAsync("Lehrer");
        var studentIds = await userManager.GetUsersInRoleAsync("Schüler");

        // Die Kachel "En attente / bloqués" zählt alle blockierten Konten, die vom
        // Administrator verwaltet werden – Schüler ebenso wie Lehrer und SchulLeiter
        // (SchulLeiter besitzen zusätzlich die Rolle "Lehrer" und erscheinen daher in
        // der Lehrer-Liste). Die Konten werden anhand ihrer Id zusammengeführt.
        // BlockedStudentCount weist zusätzlich die reine Schüleranzahl aus.
        var verwalteteBenutzer = teacherIds
            .Concat(studentIds)
            .GroupBy(x => x.Id)
            .Select(g => g.First())
            .ToList();

        return new DashboardSummary
        {
            SchoolCount = await dbContext.Schools.CountAsync(cancellationToken),
            ActiveSchoolCount = await dbContext.Schools.CountAsync(x => x.IsActive, cancellationToken),
            TeacherCount = teacherIds.Count,
            StudentCount = studentIds.Count,
            BlockedStudentCount = studentIds.Count(x => x.IsBlocked),
            BlockedAccountCount = verwalteteBenutzer.Count(x => x.IsBlocked),
            CourseCount = await dbContext.Klassen.CountAsync(cancellationToken),
            LessonCount = await dbContext.Lessons.CountAsync(cancellationToken),
            ExerciseCount = await dbContext.Exercises.CountAsync(cancellationToken)
        };
    }

    /// <summary>
    /// Liefert die Katalog-Zusammenfassung für die öffentliche Startseite direkt
    /// aus der Datenbank (Klassen/Lektionen/Übungen). Ersetzt die frühere
    /// In-Memory-JSON-Quelle, damit die Startseite den tatsächlich geseedeten
    /// Katalog (inkl. der kuratierten ~10.000 Übungen) widerspiegelt.
    /// </summary>
    public async Task<HomeCatalogViewModel> GetHomeCatalogAsync(CancellationToken cancellationToken = default)
    {
        var lessonCount = await dbContext.Lessons.CountAsync(cancellationToken);
        var exerciseCount = await dbContext.Exercises.CountAsync(cancellationToken);

        // Kursniveaus mit Lektions- und Übungszahlen (nach Klasse gruppiert).
        var levels = await dbContext.Lessons
            .AsNoTracking()
            .GroupBy(lesson => lesson.Klasse.Code)
            .Select(gruppe => new HomeCatalogLevelItem
            {
                LevelCode = gruppe.Key,
                LessonCount = gruppe.Count(),
                ExerciseCount = gruppe.Sum(lesson => lesson.Exercises.Count)
            })
            .OrderBy(item => item.LevelCode)
            .ToListAsync(cancellationToken);

        // Anzahl der Kursniveaus mit Lektionen entspricht der Anzahl der Gruppen —
        // kein zusätzlicher DB-Round-Trip nötig.
        var niveauCount = levels.Count;

        // Nur eine kleine Vorschau der ersten Lektionen laden (nicht den ganzen Katalog).
        var previewLessons = await dbContext.Lessons
            .AsNoTracking()
            .OrderBy(lesson => lesson.Klasse.Code)
            .ThenBy(lesson => lesson.Order)
            .Take(3)
            .Select(lesson => new HomeCatalogLessonItem
            {
                LevelCode = lesson.Klasse.Code,
                Title = lesson.Title,
                Summary = lesson.Summary,
                Order = lesson.Order,
                ExerciseCount = lesson.Exercises.Count
            })
            .ToListAsync(cancellationToken);

        return new HomeCatalogViewModel
        {
            NiveauCount = niveauCount,
            LessonCount = lessonCount,
            ExerciseCount = exerciseCount,
            RecommendedLesson = previewLessons.FirstOrDefault(),
            PreviewLessons = previewLessons,
            Levels = levels
        };
    }

    /// <summary>
    /// Liefert alle Lektionen des Katalogs (leichtgewichtig, mit Übungs-/Medienzahlen)
    /// für die öffentliche Lektionsübersicht. Ersetzt die frühere In-Memory-JSON-Quelle.
    /// </summary>
    public async Task<IReadOnlyList<HomeCatalogLessonItem>> GetCatalogLessonsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Lessons
            .AsNoTracking()
            .OrderBy(lesson => lesson.Klasse.Code)
            .ThenBy(lesson => lesson.Order)
            .Select(lesson => new HomeCatalogLessonItem
            {
                LevelCode = lesson.Klasse.Code,
                Title = lesson.Title,
                Summary = lesson.Summary,
                Order = lesson.Order,
                ExerciseCount = lesson.Exercises.Count,
                MediaCount = lesson.MediaItems.Count
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminSchoolOverview>> GetAdminSchoolsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Schools
            .AsNoTracking()
            .Select(school => new AdminSchoolOverview
            {
                SchoolId = school.Id,
                Name = school.Name,
                City = school.City,
                Country = school.Country,
                IsActive = school.IsActive,
                TeacherCount = school.UserSchools.Count(link => link.CanManageStudents),
                StudentCount = school.Students.Count
            })
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserManagementItem>> GetUserManagementAsync(CancellationToken cancellationToken = default)
    {
        var users = await dbContext.Users
            .AsNoTracking()
            .Include(x => x.UserSchools)
                .ThenInclude(x => x.School)
            .OrderBy(x => x.FullName)
            .ToListAsync(cancellationToken);

        var results = new List<UserManagementItem>(users.Count);
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            results.Add(new UserManagementItem
            {
                UserId = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                RoleName = roles.FirstOrDefault() ?? "Utilisateur",
                IsBlocked = user.IsBlocked,
                IsSelfRegistered = user.IsSelfRegistered,
                Schools = user.UserSchools.Select(x => x.School.Name).OrderBy(x => x).ToList()
            });
        }

        var userIds = results
            .Where(x => x.RoleName == "Schüler")
            .Select(x => x.UserId)
            .ToList();
        var levelLookup = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(x => userIds.Contains(x.AppUserId))
            .Select(x => new { x.AppUserId, x.CurrentLevelCode, x.CurrentKlasseId })
            .ToDictionaryAsync(x => x.AppUserId, x => x.CurrentLevelCode, cancellationToken);
        var classLookup = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(x => userIds.Contains(x.AppUserId))
            .Select(x => new { x.AppUserId, x.CurrentKlasseId })
            .ToDictionaryAsync(x => x.AppUserId, x => x.CurrentKlasseId, cancellationToken);
        var combinedScores = await GetCombinedProgressScoresAsync(userIds, cancellationToken);
        var paidUserIds = await dbContext.LevelEnrollments
            .AsNoTracking()
            .Where(x => userIds.Contains(x.StudentId) && x.IstAktuell && x.IsPaid)
            .Select(x => x.StudentId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var paidUserIdSet = paidUserIds.ToHashSet();
        var activeSubscriptionUserIds = await dbContext.LevelEnrollments
            .AsNoTracking()
            .Where(x => userIds.Contains(x.StudentId) && x.IstAktuell && x.IsUnlocked)
            .Select(x => x.StudentId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var activeSubscriptionUserIdSet = activeSubscriptionUserIds.ToHashSet();
        var feeLookup = await GetCurrentSchoolFeeLookupAsync(userIds, cancellationToken);
        foreach (var item in results)
        {
            item.HasPaidEnrollment = paidUserIdSet.Contains(item.UserId);
            item.IsSkoulouPlus = activeSubscriptionUserIdSet.Contains(item.UserId);
            item.HasStudentClass = classLookup.TryGetValue(item.UserId, out var currentKlasseId) && currentKlasseId.HasValue;
            if (feeLookup.TryGetValue(item.UserId, out var fee))
            {
                item.SchoolFeeAmount = fee.Amount;
                item.SchoolFeePaidAmount = fee.PaidAmount;
                item.HasActiveSchoolFeeCycle = fee.HasCycle;
            }

            if (combinedScores.TryGetValue(item.UserId, out var combined) && combined.Count > 0)
            {
                item.CombinedScorePercent = combined.AverageScorePercent;
                item.CombinedScoreCount = combined.Count;
            }

        }

        return results;
    }

    /// <summary>
    /// Ermittelt den kombinierten Fortschritts-Score aus Lektions-Übungen und Prüfungen.
    /// Der jeweils letzte bewertete Versuch je Übung (richtig = 100 %, falsch = 0 %) und jede
    /// abgeschlossene Prüfung (ScorePercent) ist ein gleichgewichteter Datenpunkt. Der Score
    /// gilt für ALLE Schüler und wird durch Wiederholungen automatisch aktualisiert.
    /// Liefert nur Einträge für Schüler mit mindestens einem Datenpunkt.
    /// Enthält zusätzlich die tatsächlich erworbenen Punkte getrennt nach Lektionsaufgaben
    /// (Übungen) und Examens im Verhältnis zur maximal erreichbaren Punktzahl, damit
    /// "erworbene Punkte von Total" separat pro Kategorie angezeigt werden kann.
    /// </summary>
    private async Task<Dictionary<Guid, CombinedProgressScore>> GetCombinedProgressScoresAsync(
        IReadOnlyCollection<Guid> studentIds,
        CancellationToken cancellationToken,
        Guid? restrictToKlasseId = null)
    {
        if (studentIds.Count == 0)
        {
            return [];
        }

        // Übungsversuche (nur bereits automatisch/manuell bewertete, keine offenen manuellen).
        var exerciseAttemptsQuery = dbContext.ExerciseAttempts
            .AsNoTracking()
            .Where(a => !a.RequiresManualGrading && studentIds.Contains(a.StudentId));

        if (restrictToKlasseId.HasValue)
        {
            exerciseAttemptsQuery = exerciseAttemptsQuery.Where(a => a.Exercise.Lesson.KlasseId == restrictToKlasseId.Value);
        }

        var exerciseAttempts = await exerciseAttemptsQuery
            .Select(a => new
            {
                a.StudentId,
                a.ExerciseId,
                LessonId = a.Exercise.LessonId,
                a.AssignmentVersion,
                a.IsCorrect,
                a.AwardedPoints,
                a.AnsweredAtUtc,
                a.Id
            })
            .ToListAsync(cancellationToken);

        // Bei neuen Abgabephasen (AssignmentVersion) pro Lektion nur die jeweils
        // neueste Phase pro Schüler berücksichtigen, damit ältere Phasen nicht
        // zusätzlich in den kombinierten Score einfließen.
        var latestLessonVersions = exerciseAttempts
            .GroupBy(a => new { a.StudentId, a.LessonId })
            .Select(group => new
            {
                group.Key.StudentId,
                group.Key.LessonId,
                AssignmentVersion = group.Max(item => item.AssignmentVersion > 0 ? item.AssignmentVersion : 1)
            })
            .ToList();

        var exerciseAttemptsForScoring = (from attempt in exerciseAttempts
                                          join latest in latestLessonVersions
                                              on new
                                              {
                                                  attempt.StudentId,
                                                  attempt.LessonId,
                                                  AssignmentVersion = attempt.AssignmentVersion > 0 ? attempt.AssignmentVersion : 1
                                              }
                                              equals new
                                              {
                                                  latest.StudentId,
                                                  latest.LessonId,
                                                  latest.AssignmentVersion
                                              }
                                          select attempt)
            .ToList();

        var exerciseIds = exerciseAttemptsForScoring.Select(a => a.ExerciseId).Distinct().ToList();
        var exercisePointsById = await dbContext.Exercises
            .AsNoTracking()
            .Where(exercise => exerciseIds.Contains(exercise.Id))
            .Select(exercise => new { exercise.Id, exercise.Points })
            .ToDictionaryAsync(x => x.Id, x => x.Points, cancellationToken);

        var examsVisibleToStudents = await dbContext.Exams
            .AsNoTracking()
            .Where(exam => exam.IsPublishedToStudents && (!restrictToKlasseId.HasValue || exam.KlasseId == restrictToKlasseId.Value))
            .Select(exam => exam.Id)
            .ToHashSetAsync(cancellationToken);

        var examAttempts = await dbContext.ExamAttempts
            .AsNoTracking()
            .Where(a => a.IsCompleted
                        && !a.RequiresManualGrading
                        && examsVisibleToStudents.Contains(a.ExamId)
                        && studentIds.Contains(a.StudentId))
            .Select(a => new
            {
                a.StudentId,
                a.ScorePercent,
                a.CorrectAnswers,
                a.TotalQuestions
            })
            .ToListAsync(cancellationToken);

        var perStudent = new Dictionary<Guid, (int ExerciseSum, int ExerciseCount, int ExercisePointsEarned, int ExercisePointsTotal, int ExamSum, int ExamCount, int ExamPointsEarned, int ExamPointsTotal)>();

        foreach (var attempt in exerciseAttemptsForScoring
            .GroupBy(a => new { a.StudentId, a.ExerciseId })
            .Select(group => group
                .OrderByDescending(a => a.AnsweredAtUtc)
                .ThenByDescending(a => a.Id)
                .First()))
        {
            perStudent.TryGetValue(attempt.StudentId, out var acc);
            var hasExercisePoints = exercisePointsById.TryGetValue(attempt.ExerciseId, out var configuredPoints);
            var maxPoints = configuredPoints > 0 ? configuredPoints : 10;
            var earnedPoints = attempt.AwardedPoints > 0
                ? Math.Clamp(attempt.AwardedPoints, 0, maxPoints)
                : (attempt.IsCorrect ? maxPoints : 0);
            var percent = (int)Math.Round(earnedPoints * 100d / maxPoints);
            perStudent[attempt.StudentId] = acc with
            {
                ExerciseSum = acc.ExerciseSum + percent,
                ExerciseCount = acc.ExerciseCount + 1,
                ExercisePointsEarned = acc.ExercisePointsEarned + earnedPoints,
                ExercisePointsTotal = acc.ExercisePointsTotal + maxPoints
            };
        }

        foreach (var attempt in examAttempts)
        {
            perStudent.TryGetValue(attempt.StudentId, out var acc);
            var examMaxPoints = attempt.TotalQuestions > 0 ? attempt.TotalQuestions : 1;
            var examEarnedPoints = attempt.TotalQuestions > 0 ? attempt.CorrectAnswers : (attempt.ScorePercent >= 50 ? 1 : 0);
            perStudent[attempt.StudentId] = acc with
            {
                ExamSum = acc.ExamSum + attempt.ScorePercent,
                ExamCount = acc.ExamCount + 1,
                ExamPointsEarned = acc.ExamPointsEarned + examEarnedPoints,
                ExamPointsTotal = acc.ExamPointsTotal + examMaxPoints
            };
        }

        {
            var now = DateTime.UtcNow;
            var studentKlasseById = await dbContext.StudentProfiles
                .AsNoTracking()
                .Where(p => studentIds.Contains(p.AppUserId) && p.CurrentKlasseId.HasValue)
                .Select(p => new { p.AppUserId, KlasseId = p.CurrentKlasseId!.Value })
                .ToDictionaryAsync(p => p.AppUserId, p => p.KlasseId, cancellationToken);
            if (restrictToKlasseId.HasValue)
            {
                studentKlasseById = studentIds.Where(studentKlasseById.ContainsKey)
                    .Where(id => studentKlasseById[id] == restrictToKlasseId.Value)
                    .ToDictionary(id => id, id => restrictToKlasseId.Value);
            }

            var klasseIdsForOverdue = studentKlasseById.Values.Distinct().ToList();
            var overdueExercises = await dbContext.Exercises
                .AsNoTracking()
                .Where(e => klasseIdsForOverdue.Contains(e.Lesson.KlasseId)
                            && e.Lesson.IsPublished
                            && e.Lesson.ExerciseSubmissionDueAtUtc.HasValue
                            && e.Lesson.ExerciseSubmissionDueAtUtc.Value <= now)
                .Select(e => new { e.Id, e.Points, KlasseId = e.Lesson.KlasseId })
                .ToListAsync(cancellationToken);

            if (overdueExercises.Count > 0)
            {
                var attemptedPairs = await dbContext.ExerciseAttempts
                    .AsNoTracking()
                    .Where(a => studentIds.Contains(a.StudentId) && klasseIdsForOverdue.Contains(a.Exercise.Lesson.KlasseId))
                    .Select(a => new { a.StudentId, a.ExerciseId })
                    .Distinct()
                    .ToListAsync(cancellationToken);
                var attemptedSet = attemptedPairs.Select(p => (p.StudentId, p.ExerciseId)).ToHashSet();

                foreach (var (studentId, studentKlasseId) in studentKlasseById)
                {
                    foreach (var exercise in overdueExercises.Where(e => e.KlasseId == studentKlasseId))
                    {
                        if (attemptedSet.Contains((studentId, exercise.Id)))
                        {
                            continue;
                        }

                        perStudent.TryGetValue(studentId, out var acc);
                        perStudent[studentId] = acc with
                        {
                            ExerciseCount = acc.ExerciseCount + 1,
                            ExercisePointsTotal = acc.ExercisePointsTotal + (exercise.Points > 0 ? exercise.Points : 10)
                        };
                    }
                }
            }
        }

        return perStudent.ToDictionary(
            x => x.Key,
            x =>
            {
                var totalCount = x.Value.ExerciseCount + x.Value.ExamCount;
                var pointsEarned = x.Value.ExercisePointsEarned + x.Value.ExamPointsEarned;
                var pointsTotal = x.Value.ExercisePointsTotal + x.Value.ExamPointsTotal;
                return new CombinedProgressScore(
                    pointsTotal > 0 ? (int)Math.Round(pointsEarned * 100d / pointsTotal) : 0,
                    totalCount,
                    x.Value.ExercisePointsEarned,
                    x.Value.ExercisePointsTotal,
                    x.Value.ExamPointsEarned,
                    x.Value.ExamPointsTotal);
            });
    }

    /// <summary>
    /// Kombinierter Fortschritts-Score eines Schülers, getrennt nach Lektionsaufgaben
    /// (Übungen) und Examens, damit "erworbene Punkte von Total" pro Kategorie angezeigt
    /// werden kann.
    /// </summary>
    private readonly record struct CombinedProgressScore(
        int AverageScorePercent,
        int Count,
        int ExercisePointsEarned,
        int ExercisePointsTotal,
        int ExamPointsEarned,
        int ExamPointsTotal)
    {
        public int PointsEarned => ExercisePointsEarned + ExamPointsEarned;
        public int PointsTotal => ExercisePointsTotal + ExamPointsTotal;
    }

    public async Task<IReadOnlyList<CourseManagementViewModel>> GetCourseManagementAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Klassen
            .AsNoTracking()
            .Include(x => x.Lessons)
                .ThenInclude(x => x.Exercises)
            .Include(x => x.Lessons)
                .ThenInclude(x => x.MediaItems)
            .Include(x => x.Exams)
            .Include(x => x.School)
            .OrderBy(x => x.Code)
            .Select(course => new CourseManagementViewModel
            {
                CourseId = course.Id,
                SchoolId = course.SchoolId,
                SchoolName = course.School != null ? course.School.Name : null,
                IstSystemNiveau = course.IstSystemNiveau,
                Code = course.Code,
                Title = course.Title,
                CEFRGroup = course.CEFRGroup,
                Description = course.Description,
                RecommendedLessonCount = course.RecommendedLessonCount,
                ExamCount = course.Exams.Count,
                MediaCount = course.Lessons.SelectMany(x => x.MediaItems).Count(),
                Price = course.Price,
                MaxPlaetze = course.MaxPlaetze,
                StudentCount = course.Enrollments.Count(x => x.IstAktuell && x.IsUnlocked),
                Lessons = course.Lessons
                    .OrderBy(x => x.Order)
                    .Select(lesson => new LessonManagementItem
                    {
                        LessonId = lesson.Id,
                        Title = lesson.Title,
                        Summary = lesson.Summary,
                        Order = lesson.Order,
                        ExerciseCount = lesson.Exercises.Count
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MediaLibraryItem>> GetMediaLibraryAsync(CancellationToken cancellationToken = default)
    {
        var blobMedia = await dbContext.BlobMediaAssets
            .AsNoTracking()
            .Include(x => x.School)
            .Include(x => x.Klasse)
            .OrderBy(x => x.School.Name)
            .ThenBy(x => x.DisplayName)
            .ThenBy(x => x.OriginalFileName)
            .Select(asset => new MediaLibraryItem
            {
                MediaId = asset.Id,
                SchoolId = asset.SchoolId,
                SchoolName = asset.School.Name,
                MediaType = asset.ContentType,
                RelativePath = asset.BlobName,
                DisplayName = asset.DisplayName,
                OriginalFileName = asset.OriginalFileName,
                ContentType = asset.ContentType,
                Length = asset.Length,
                AltText = asset.AltText,
                LessonTitle = string.Empty,
                CourseCode = asset.Klasse != null ? asset.Klasse.Code : string.Empty,
                KlasseCode = asset.Klasse != null ? asset.Klasse.Code : string.Empty,
                StorageHint = "Médiathèque sécurisée"
            })
            .ToListAsync(cancellationToken);

        return blobMedia;
    }

    public async Task<StudentDashboardViewModel?> GetStudentDashboardAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var profile = await dbContext.StudentProfiles
            .AsNoTracking()
            .Include(x => x.AppUser)
            .Include(x => x.School)
            .FirstOrDefaultAsync(x => x.AppUserId == userId, cancellationToken);

        if (profile is null)
        {
            return null;
        }

        var hasCurrentKlasseAssignment = profile.CurrentKlasseId.HasValue;

        var currentClassCycle = hasCurrentKlasseAssignment
            ? await dbContext.KursNiveauDurchlaeufe
                .AsNoTracking()
                .Where(run => run.KlasseId == profile.CurrentKlasseId!.Value)
                .OrderByDescending(run => run.StartDatum)
                .ThenByDescending(run => run.ErstelltAmUtc)
                .Select(run => new { run.EndDatum })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var hasCurrentClassCycle = currentClassCycle is not null;
        var isCurrentClassCycleClosed = currentClassCycle?.EndDatum is not null;

        var currentClassEnrollment = hasCurrentKlasseAssignment
            ? await dbContext.LevelEnrollments
                .AsNoTracking()
                .Where(enrollment => enrollment.StudentId == userId && enrollment.KlasseId == profile.CurrentKlasseId!.Value && enrollment.IstAktuell)
                .Select(enrollment => new { enrollment.IsUnlocked, enrollment.IsPaid })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var courseProgress = await dbContext.Klassen
            .AsNoTracking()
            .Include(x => x.Lessons)
                .ThenInclude(x => x.Exercises)
            .Include(x => x.Exams)
            .Where(x => x.SchoolId == profile.SchoolId)
            .OrderBy(x => x.Code)
            .Select(course => new StudentCourseProgressItem
            {
                CourseCode = course.Code,
                CourseTitle = course.Title,
                LessonCount = course.Lessons.Count,
                ExerciseCount = course.Lessons.SelectMany(x => x.Exercises).Count(),
                ExamCount = course.Exams.Count,
                IsCurrentLevel = profile.CurrentKlasseId.HasValue
                    ? course.Id == profile.CurrentKlasseId.Value
                    : course.Code == profile.CurrentLevelCode,
                IsUnlocked = false
            })
            .ToListAsync(cancellationToken);

        var unlockedKlasseIds = await dbContext.LevelEnrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.StudentId == userId && enrollment.IstAktuell && enrollment.IsUnlocked)
            .Select(enrollment => enrollment.KlasseId)
            .ToHashSetAsync(cancellationToken);
        var klasseIdsByCode = await dbContext.Klassen
            .AsNoTracking()
            .Where(klasse => klasse.SchoolId == profile.SchoolId)
            .Select(klasse => new { klasse.Id, klasse.Code })
            .ToDictionaryAsync(klasse => klasse.Code, klasse => klasse.Id, cancellationToken);
        foreach (var course in courseProgress)
        {
            course.IsUnlocked = klasseIdsByCode.TryGetValue(course.CourseCode, out var klasseId)
                && unlockedKlasseIds.Contains(klasseId);
        }

        // Abschlussprüfung nur freischalten, wenn alle Lektionen des aktuellen Niveaus
        // abgeschlossen sind (eine Lektion gilt erst als abgeschlossen, wenn ihre Übungen bestanden wurden).
        var currentLevelLessonIds = await dbContext.Lessons
            .AsNoTracking()
            .Where(x => profile.CurrentKlasseId.HasValue
                ? x.KlasseId == profile.CurrentKlasseId.Value
                : x.Klasse.SchoolId == profile.SchoolId && x.Klasse.Code == profile.CurrentLevelCode)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var completedLessonIds = await dbContext.LessonProgresses
            .AsNoTracking()
            .Where(x => x.StudentId == userId && x.IsCompleted && currentLevelLessonIds.Contains(x.LessonId))
            .Select(x => x.LessonId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var canTakeFinalExam = currentLevelLessonIds.Count > 0
            && completedLessonIds.Count == currentLevelLessonIds.Count;

        var openLessonsCount = currentLevelLessonIds.Count - completedLessonIds.Count;

        var openExamsCount = 0;
        if (hasCurrentKlasseAssignment)
        {
            var currentLevelExamIds = await dbContext.Exams
                .AsNoTracking()
                .Where(x => x.KlasseId == profile.CurrentKlasseId!.Value && x.IsPublishedToStudents)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            var passedExamIds = await dbContext.ExamAttempts
                .AsNoTracking()
                .Where(x => x.StudentId == userId && x.IsPassed && currentLevelExamIds.Contains(x.ExamId))
                .Select(x => x.ExamId)
                .Distinct()
                .ToListAsync(cancellationToken);

            openExamsCount = currentLevelExamIds.Count - passedExamIds.Count;
        }

        var combinedScores = await GetCombinedProgressScoresAsync([userId], cancellationToken, profile.CurrentKlasseId);
        combinedScores.TryGetValue(userId, out var combined);
        var hasActiveSubscription = unlockedKlasseIds.Count > 0;
        var isCurrentClassUnlocked = currentClassEnrollment?.IsUnlocked == true;
        var isCurrentClassPaid = currentClassEnrollment?.IsPaid == true;
        var hasPaidSkoulouAppAbo = await dbContext.LevelEnrollments
            .AsNoTracking()
            .AnyAsync(enrollment => enrollment.StudentId == userId && enrollment.IstAktuell && enrollment.IsPaid, cancellationToken);

        var (streakDays, lastActivityAtUtc) = await GetActivitySummaryAsync(userId, profile.CurrentKlasseId, cancellationToken);
        var classHistory = await GetStudentClassHistoryAsync(userId, profile.SchoolId, profile.CurrentKlasseId, cancellationToken);

        return new StudentDashboardViewModel
        {
            FullName = profile.AppUser.FullName,
            SchoolName = profile.School.Name,
            CurrentLevelCode = profile.CurrentLevelCode ?? string.Empty,
            HasCurrentKlasseAssignment = hasCurrentKlasseAssignment,
            HasCurrentClassCycle = hasCurrentClassCycle,
            IsCurrentClassCycleClosed = isCurrentClassCycleClosed,
            IsCurrentClassUnlocked = isCurrentClassUnlocked,
            IsCurrentClassPaid = isCurrentClassPaid,
            CompletedLessonsCount = profile.CompletedLessonsCount,
            IsBlocked = profile.AppUser.IsBlocked,
            CanTakeFinalExam = canTakeFinalExam,
            IsSkoulouPlus = false,
            HasActiveSubscription = hasActiveSubscription,
            HasPaidSkoulouAppAbo = hasPaidSkoulouAppAbo,
            CombinedScorePercent = combined.Count > 0 ? combined.AverageScorePercent : null,
            CombinedScoreCount = combined.Count,
            ExercisePointsEarned = combined.ExercisePointsEarned,
            ExercisePointsTotal = combined.ExercisePointsTotal,
            ExamPointsEarned = combined.ExamPointsEarned,
            ExamPointsTotal = combined.ExamPointsTotal,
            StreakDays = streakDays,
            LastActivityAtUtc = lastActivityAtUtc,
            CourseProgress = courseProgress,
            ClassHistory = classHistory,
            OpenLessonsCount = Math.Max(0, openLessonsCount),
            OpenExamsCount = Math.Max(0, openExamsCount)
        };
    }

    private async Task<List<StudentClassHistoryItem>> GetStudentClassHistoryAsync(
        Guid studentId,
        Guid schoolId,
        Guid? currentKlasseId,
        CancellationToken cancellationToken)
    {
        var cycles = await dbContext.KursNiveauDurchlaeufe.AsNoTracking()
            .Where(cycle => cycle.SchulID == schoolId
                && (cycle.EndDatum == null && cycle.KlasseId == currentKlasseId
                    || dbContext.KlassenAbschlussEntscheidungen.Any(result => result.DurchlaufId == cycle.Id && result.StudentId == studentId)
                    || dbContext.LevelEnrollments.Any(enrollment => enrollment.StudentId == studentId && enrollment.DurchlaufId == cycle.Id)))
            .Select(cycle => new { cycle.Id, cycle.KlasseId, cycle.StartDatum, cycle.EndDatum })
            .OrderByDescending(cycle => cycle.StartDatum)
            .ToListAsync(cancellationToken);
        var klasseIds = cycles.Select(cycle => cycle.KlasseId).Distinct().ToList();
        if (klasseIds.Count == 0)
        {
            return [];
        }

        var classes = await dbContext.Klassen
            .AsNoTracking()
            .Where(klasse => klasseIds.Contains(klasse.Id) && klasse.SchoolId == schoolId)
            .OrderBy(klasse => klasse.Code)
            .Select(klasse => new { klasse.Id, klasse.Code, klasse.Title })
            .ToListAsync(cancellationToken);

        var lessons = await dbContext.TeacherLessons
            .AsNoTracking()
            .Include(lesson => lesson.Klasse)
            .Include(lesson => lesson.Author)
            .Include(lesson => lesson.Pages)
            .Where(lesson => klasseIds.Contains(lesson.KlasseId)
                && lesson.IsPublished
                && lesson.AuthorId.HasValue
                && lesson.Author!.UserSchools.Any(link => link.SchoolId == schoolId))
            .OrderBy(lesson => lesson.Klasse.Code)
            .ThenBy(lesson => lesson.Order)
            .ToListAsync(cancellationToken);

        var exerciseAttempts = await dbContext.ExerciseAttempts
            .AsNoTracking()
            .Where(attempt => attempt.StudentId == studentId
                && !attempt.RequiresManualGrading
                && klasseIds.Contains(attempt.Exercise.Lesson.KlasseId))
            .Select(attempt => new
            {
                attempt.ExerciseId,
                KlasseId = attempt.Exercise.Lesson.KlasseId,
                attempt.IsCorrect,
                attempt.AwardedPoints,
                MaxPoints = attempt.Exercise.Points,
                attempt.AnsweredAtUtc,
                attempt.Id
            })
            .ToListAsync(cancellationToken);
        var examAttempts = await dbContext.ExamAttempts
            .AsNoTracking()
            .Where(attempt => attempt.StudentId == studentId
                && attempt.IsCompleted
                && !attempt.RequiresManualGrading
                && klasseIds.Contains(attempt.Exam.KlasseId))
            .Select(attempt => new { KlasseId = attempt.Exam.KlasseId, attempt.ScorePercent, attempt.CorrectAnswers, attempt.TotalQuestions, attempt.SubmittedAtUtc })
            .ToListAsync(cancellationToken);
        var attemptedExerciseIds = (await dbContext.ExerciseAttempts
            .AsNoTracking()
            .Where(attempt => attempt.StudentId == studentId && klasseIds.Contains(attempt.Exercise.Lesson.KlasseId))
            .Select(attempt => attempt.ExerciseId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();
        var nowUtc = DateTime.UtcNow;
        var overdueExercises = await dbContext.Exercises
            .AsNoTracking()
            .Where(e => klasseIds.Contains(e.Lesson.KlasseId)
                && e.Lesson.IsPublished
                && e.Lesson.ExerciseSubmissionDueAtUtc.HasValue
                && e.Lesson.ExerciseSubmissionDueAtUtc.Value <= nowUtc)
            .Select(e => new { e.Id, e.Points, KlasseId = e.Lesson.KlasseId, DueAtUtc = e.Lesson.ExerciseSubmissionDueAtUtc!.Value })
            .ToListAsync(cancellationToken);

        var entscheidungen = await dbContext.KlassenAbschlussEntscheidungen
            .AsNoTracking()
            .Where(x => x.StudentId == studentId && klasseIds.Contains(x.Durchlauf.KlasseId))
            .Select(x => new
            {
                x.DurchlaufId,
                x.AbschlussNoteText,
                x.MentionText,
                x.AbschlussNote,
                x.IsBestanden
            })
            .ToListAsync(cancellationToken);

        return cycles.Select(cycle =>
        {
            var klasse = classes.First(item => item.Id == cycle.KlasseId);
            var scores = exerciseAttempts
                .Where(attempt => attempt.KlasseId == klasse.Id && attempt.AnsweredAtUtc >= cycle.StartDatum
                    && (cycle.EndDatum == null || attempt.AnsweredAtUtc <= cycle.EndDatum.Value.Date.AddDays(1).AddTicks(-1)))
                .GroupBy(attempt => attempt.ExerciseId)
                .Select(group =>
                {
                    var latest = group.OrderByDescending(attempt => attempt.AnsweredAtUtc).ThenByDescending(attempt => attempt.Id).First();
                    var max = latest.MaxPoints > 0 ? latest.MaxPoints : 10;
                    var earned = latest.AwardedPoints > 0 ? Math.Clamp(latest.AwardedPoints, 0, max) : (latest.IsCorrect ? max : 0);
                    return (Earned: (double)earned, Max: (double)max);
                })
                .Concat(overdueExercises
                    .Where(e => e.KlasseId == klasse.Id && !attemptedExerciseIds.Contains(e.Id) && e.DueAtUtc >= cycle.StartDatum
                        && (cycle.EndDatum == null || e.DueAtUtc <= cycle.EndDatum.Value.Date.AddDays(1).AddTicks(-1)))
                    .Select(e => (Earned: 0d, Max: (double)(e.Points > 0 ? e.Points : 10))))
                .Concat(examAttempts.Where(attempt => attempt.KlasseId == klasse.Id && attempt.SubmittedAtUtc >= cycle.StartDatum
                    && (cycle.EndDatum == null || attempt.SubmittedAtUtc <= cycle.EndDatum.Value.Date.AddDays(1).AddTicks(-1)))
                    .Select(attempt => attempt.TotalQuestions > 0
                        ? (Earned: (double)attempt.CorrectAnswers, Max: (double)attempt.TotalQuestions)
                        : (Earned: attempt.ScorePercent >= 50 ? 1d : 0d, Max: 1d)))
                .ToList();

            var abschluss = entscheidungen.FirstOrDefault(e => e.DurchlaufId == cycle.Id);

            return new StudentClassHistoryItem
            {
                DurchlaufId = cycle.Id,
                StartDatum = cycle.StartDatum,
                EndDatum = cycle.EndDatum,
                KlasseId = klasse.Id,
                CourseCode = klasse.Code,
                CourseTitle = klasse.Title,
                IsCurrent = cycle.EndDatum == null && klasse.Id == currentKlasseId,
                ScorePercent = scores.Sum(s => s.Max) > 0 ? (int)Math.Round(scores.Sum(s => s.Earned) * 100d / scores.Sum(s => s.Max)) : null,
                ScoreCount = scores.Count,
                AbschlussNoteText = !string.IsNullOrWhiteSpace(abschluss?.AbschlussNoteText)
                    ? abschluss!.AbschlussNoteText
                    : abschluss?.AbschlussNote?.ToString("N2"),
                MentionText = abschluss?.MentionText,
                IsBestanden = abschluss?.IsBestanden,
                AbschlussExamCount = !string.IsNullOrWhiteSpace(abschluss?.AbschlussNoteText) ? 1 : 0,
                TeacherLessons = lessons
                    .Where(lesson => lesson.KlasseId == klasse.Id)
                    .Select(lesson =>
                    {
                        var item = new TeacherLessonListItem
                        {
                            Id = lesson.Id,
                            Title = lesson.Title,
                            Summary = lesson.Summary,
                            CourseTheme = lesson.CourseTheme,
                            CourseCode = lesson.Klasse.Code,
                            KlasseId = lesson.KlasseId,
                            Order = lesson.Order,
                            IsPublished = lesson.IsPublished,
                            PageCount = lesson.Pages.Count,
                            AuthorName = lesson.Author?.FullName ?? string.Empty,
                            AuthorId = lesson.AuthorId,
                            IsPdfExportEnabled = lesson.IsPdfExportEnabled,
                            IsSharedWithSchool = lesson.IsSharedWithSchool
                        };
                        return item;
                    })
                    .ToList()
            };
        }).OrderByDescending(item => item.IsCurrent).ThenByDescending(item => item.StartDatum).ToList();
    }

    private async Task<HashSet<Guid>> GetUnlockedStandardKlasseIdsAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var explicitlyUnlockedIds = await dbContext.LevelEnrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.StudentId == studentId && enrollment.IstAktuell && enrollment.IsUnlocked)
            .Select(enrollment => enrollment.KlasseId)
            .ToListAsync(cancellationToken);

        var subscribedCefrGroups = await dbContext.LevelEnrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.StudentId == studentId && enrollment.IstAktuell && enrollment.IsUnlocked)
            .Select(enrollment => enrollment.Klasse.CEFRGroup)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Distinct()
            .ToListAsync(cancellationToken);

        if (subscribedCefrGroups.Count == 0)
        {
            return explicitlyUnlockedIds.ToHashSet();
        }

        var groupedIds = await dbContext.Klassen
            .AsNoTracking()
            .Where(klasse => klasse.SchoolId == null
                && klasse.IstSystemNiveau
                && subscribedCefrGroups.Contains(klasse.CEFRGroup))
            .Select(klasse => klasse.Id)
            .ToListAsync(cancellationToken);

        var result = explicitlyUnlockedIds.ToHashSet();
        result.UnionWith(groupedIds);
        return result;
    }

    /// <summary>
    /// Ermittelt die aktuelle Tages-Streak und die gesammelten XP-Punkte eines Schülers.
    /// XP: 10 pro richtig beantworteter Übung, 20 pro abgeschlossener Lektion, je 1 Punkt pro
    /// ScorePercent bei abgeschlossenen Prüfungen.
    /// Streak: Anzahl aufeinanderfolgender Kalendertage (endend heute oder gestern) mit
    /// mindestens einer Aktivität (Übung, Lektion oder Prüfung).
    /// </summary>
    private async Task<(int StreakDays, DateTime? LastActivityAtUtc)> GetActivitySummaryAsync(Guid userId, Guid? currentKlasseId, CancellationToken cancellationToken)
    {
        var exerciseAttemptsQuery = dbContext.ExerciseAttempts
            .AsNoTracking()
            .Where(a => a.StudentId == userId);

        if (currentKlasseId.HasValue)
        {
            exerciseAttemptsQuery = exerciseAttemptsQuery.Where(a => a.Exercise.Lesson.KlasseId == currentKlasseId.Value);
        }

        var exerciseAttempts = await exerciseAttemptsQuery
            .Select(a => a.AnsweredAtUtc)
            .ToListAsync(cancellationToken);

        var lessonCompletionsQuery = dbContext.LessonProgresses
            .AsNoTracking()
            .Where(x => x.StudentId == userId && x.IsCompleted && x.CompletedAtUtc != null);

        if (currentKlasseId.HasValue)
        {
            lessonCompletionsQuery = lessonCompletionsQuery.Where(x => x.Lesson.KlasseId == currentKlasseId.Value);
        }

        var lessonCompletions = await lessonCompletionsQuery
            .Select(x => x.CompletedAtUtc!.Value)
            .ToListAsync(cancellationToken);

        var examAttemptsQuery = dbContext.ExamAttempts
            .AsNoTracking()
            .Where(a => a.StudentId == userId && a.IsCompleted && a.SubmittedAtUtc != null);

        if (currentKlasseId.HasValue)
        {
            examAttemptsQuery = examAttemptsQuery.Where(a => a.Exam.KlasseId == currentKlasseId.Value);
        }

        var examAttempts = await examAttemptsQuery
            .Select(a => a.SubmittedAtUtc!.Value)
            .ToListAsync(cancellationToken);

        var activityTimes = exerciseAttempts.Concat(lessonCompletions).Concat(examAttempts).ToList();
        var activityDates = activityTimes.Select(d => d.Date).ToHashSet();

        var streakDays = 0;
        if (activityDates.Count > 0)
        {
            var today = DateTime.UtcNow.Date;
            var cursor = activityDates.Contains(today) ? today : today.AddDays(-1);

            while (activityDates.Contains(cursor))
            {
                streakDays++;
                cursor = cursor.AddDays(-1);
            }
        }

        return (streakDays, activityTimes.Count > 0 ? activityTimes.Max() : null);
    }

    public async Task<IReadOnlyList<SchoolManagementViewModel>> GetSchoolManagementAsync(CancellationToken cancellationToken = default)
    {
        var teacherIds = (await userManager.GetUsersInRoleAsync("Lehrer")).Select(x => x.Id).ToHashSet();
        var directorIds = (await userManager.GetUsersInRoleAsync(Rollen.SchulLeiter)).Select(x => x.Id).ToHashSet();
        var globalSubscriptionPrice = await GetGlobalSubscriptionPriceAsync(cancellationToken);

        return await dbContext.Schools
            .AsNoTracking()
            .Include(x => x.UserSchools)
                .ThenInclude(x => x.AppUser)
            .Include(x => x.Students)
            .OrderBy(x => x.Name)
            .Select(school => new SchoolManagementViewModel
            {
                SchoolId = school.Id,
                Name = school.Name,
                City = school.City,
                Country = school.Country,
                IsActive = school.IsActive,
                TeacherCount = school.UserSchools.Count(link => link.CanManageStudents && teacherIds.Contains(link.AppUserId)),
                StudentCount = school.Students.Count,
                OutstandingDebt = school.Students
                    .SelectMany(student => student.AppUser.LevelEnrollments)
                    .Where(enrollment => enrollment.IsPaid
                        && enrollment.AbgerechnetInPeriodeId == null
                        && enrollment.PriceAtEnrollment > 0m
                        && (enrollment.SchoolLeaderCommissionAtEnrollment > 0m
                            || enrollment.TeacherCommissionAtEnrollment > 0m
                            || enrollment.ProductOwnerCommissionAtEnrollment > 0m))
                    .Sum(enrollment => enrollment.PriceAtEnrollment),
                EffectiveSubscriptionPrice = school.SubscriptionPriceOverride ?? globalSubscriptionPrice,
                SubscriptionPriceOverride = school.SubscriptionPriceOverride,
                DirectorAssignments = school.UserSchools
                    .Where(link => link.CanManageStudents && directorIds.Contains(link.AppUserId))
                    .OrderBy(link => link.AppUser.FullName)
                    .Select(link => new TeacherAssignmentOption
                    {
                        TeacherId = link.AppUserId,
                        TeacherName = link.AppUser.FullName,
                        Email = link.AppUser.Email ?? string.Empty,
                        IsAssigned = true
                    })
                    .ToList(),
                TeacherAssignments = school.UserSchools
                    .Where(link => link.CanManageStudents && teacherIds.Contains(link.AppUserId))
                    .OrderBy(link => link.AppUser.FullName)
                    .Select(link => new TeacherAssignmentOption
                    {
                        TeacherId = link.AppUserId,
                        TeacherName = link.AppUser.FullName,
                        Email = link.AppUser.Email ?? string.Empty,
                        IsAssigned = true
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TeacherAssignmentOption>> GetTeacherOptionsAsync(Guid? schoolId = null, CancellationToken cancellationToken = default)
    {
        var teachers = await userManager.GetUsersInRoleAsync("Lehrer");
        var assignedTeacherIds = schoolId.HasValue
            ? await dbContext.UserSchools
                .AsNoTracking()
                .Where(link => link.SchoolId == schoolId.Value && link.CanManageStudents)
                .Select(link => link.AppUserId)
                .ToListAsync(cancellationToken)
            : [];

        return teachers
            .OrderBy(x => x.FullName)
            .Select(teacher => new TeacherAssignmentOption
            {
                TeacherId = teacher.Id,
                TeacherName = teacher.FullName,
                Email = teacher.Email ?? string.Empty,
                IsAssigned = assignedTeacherIds.Contains(teacher.Id)
            })
            .ToList();
    }

    public async Task<IReadOnlyList<TeacherAssignmentOption>> GetSchoolOptionsAsync(Guid? userId = null, CancellationToken cancellationToken = default)
    {
        var assignedSchoolIds = userId.HasValue
            ? await dbContext.UserSchools
                .AsNoTracking()
                .Where(link => link.AppUserId == userId.Value)
                .Select(link => link.SchoolId)
                .ToListAsync(cancellationToken)
            : [];

        return await dbContext.Schools
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(school => new TeacherAssignmentOption
            {
                TeacherId = school.Id,
                TeacherName = school.Name,
                Email = $"{school.City}, {school.Country}",
                IsAssigned = assignedSchoolIds.Contains(school.Id)
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<(bool Succeeded, string Message)> SaveSchoolAsync(SchoolFormModel model, IReadOnlyCollection<Guid> teacherIds, CancellationToken cancellationToken = default)
    {
        var normalizedName = model.Name.Trim();
        var duplicateExists = await dbContext.Schools.AnyAsync(
            x => x.Name == normalizedName && (!model.Id.HasValue || x.Id != model.Id.Value),
            cancellationToken);

        if (duplicateExists)
        {
            return (false, "Une école avec ce nom existe déjà.");
        }

        School school;
        if (model.Id.HasValue)
        {
            school = await dbContext.Schools.FirstOrDefaultAsync(x => x.Id == model.Id.Value, cancellationToken)
                ?? new School();

            if (school.Id == Guid.Empty)
            {
                return (false, "L'école est introuvable.");
            }
        }
        else
        {
            school = new School();
            await dbContext.Schools.AddAsync(school, cancellationToken);
        }

        school.Name = normalizedName;
        school.City = model.City.Trim();
        school.Country = model.Country.Trim();
        school.DescriptionHtml = HtmlSanitizer.Sanitize(model.DescriptionHtml);
        school.IsActive = model.IsActive;
        school.SubscriptionPriceOverride = model.SubscriptionPriceOverride;
        school.SchoolLeaderCommissionPercentOverride = model.SchoolLeaderCommissionPercentOverride;
        school.TeacherCommissionPercentOverride = model.TeacherCommissionPercentOverride;
        school.ProductOwnerCommissionPercentOverride = model.ProductOwnerCommissionPercentOverride;

        var commissionOverrideSum = (school.SchoolLeaderCommissionPercentOverride ?? 0m)
            + (school.TeacherCommissionPercentOverride ?? 0m)
            + (school.ProductOwnerCommissionPercentOverride ?? 0m);
        if (commissionOverrideSum > 100m)
        {
            return (false, "La somme des commissions spécifiques de l'école ne peut pas dépasser 100 %.");
        }

        school.GeaendertAmUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await SyncTeacherAssignmentsAsync(school.Id, teacherIds, cancellationToken);

        return (true, model.Id.HasValue ? "L'école a été mise à jour." : "L'école a été créée.");
    }

    /// <summary>Erstellt eine neue Schule und ordnet sie ausschließlich dem angemeldeten Schulleiter zu.</summary>
    public async Task<(bool Succeeded, string Message)> CreateSchoolForSchoolLeaderAsync(
        Guid schoolLeaderId,
        SchoolFormModel model,
        CancellationToken cancellationToken = default)
    {
        var schoolLeader = await userManager.FindByIdAsync(schoolLeaderId.ToString());
        if (schoolLeader is null || !await userManager.IsInRoleAsync(schoolLeader, Rollen.SchulLeiter))
        {
            return (false, "Seul un directeur peut créer une école.");
        }

        var normalizedName = model.Name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || string.IsNullOrWhiteSpace(model.City) || string.IsNullOrWhiteSpace(model.Country))
        {
            return (false, "Veuillez renseigner le nom, la ville et le pays de l'école.");
        }

        if (await dbContext.Schools.AnyAsync(school => school.Name == normalizedName, cancellationToken))
        {
            return (false, "Une école avec ce nom existe déjà.");
        }

        var school = new School
        {
            Name = normalizedName,
            City = model.City.Trim(),
            Country = model.Country.Trim(),
            DescriptionHtml = HtmlSanitizer.Sanitize(model.DescriptionHtml),
            IsActive = true
        };

        await dbContext.Schools.AddAsync(school, cancellationToken);
        await dbContext.UserSchools.AddAsync(new UserSchool
        {
            AppUserId = schoolLeaderId,
            SchoolId = school.Id,
            CanManageStudents = true
        }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return (true, "L'école a été créée et t'est maintenant attribuée.");
    }

    public async Task<(bool Succeeded, string Message)> UpdateSchoolForSchoolLeaderAsync(
        Guid schoolLeaderId,
        SchoolFormModel model,
        CancellationToken cancellationToken = default)
    {
        if (!model.Id.HasValue)
        {
            return (false, "L'école est introuvable.");
        }

        var schoolLeader = await userManager.FindByIdAsync(schoolLeaderId.ToString());
        if (schoolLeader is null || (!await userManager.IsInRoleAsync(schoolLeader, Rollen.SchulLeiter) && !await userManager.IsInRoleAsync(schoolLeader, Rollen.Koordinator)))
        {
            return (false, "Seule la direction peut modifier cette école.");
        }

        var normalizedName = model.Name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || string.IsNullOrWhiteSpace(model.City) || string.IsNullOrWhiteSpace(model.Country))
        {
            return (false, "Veuillez renseigner le nom, la ville et le pays de l'école.");
        }

        var hasAccess = await dbContext.UserSchools
            .AsNoTracking()
            .AnyAsync(link => link.AppUserId == schoolLeaderId && link.SchoolId == model.Id.Value && link.CanManageStudents, cancellationToken);
        if (!hasAccess)
        {
            return (false, "Vous ne pouvez pas modifier cette école.");
        }

        var duplicateExists = await dbContext.Schools
            .AnyAsync(school => school.Name == normalizedName && school.Id != model.Id.Value, cancellationToken);
        if (duplicateExists)
        {
            return (false, "Une école avec ce nom existe déjà.");
        }

        var school = await dbContext.Schools.FirstOrDefaultAsync(s => s.Id == model.Id.Value, cancellationToken);
        if (school is null)
        {
            return (false, "L'école est introuvable.");
        }

        school.Name = normalizedName;
        school.City = model.City.Trim();
        school.Country = model.Country.Trim();
        school.DescriptionHtml = HtmlSanitizer.Sanitize(model.DescriptionHtml);
        school.IsActive = model.IsActive;
        school.GeaendertAmUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, "L'école a été mise à jour.");
    }

    public async Task<SchoolFormModel?> GetSchoolForEditAsync(Guid schoolId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Schools
            .AsNoTracking()
            .Where(x => x.Id == schoolId)
            .Select(x => new SchoolFormModel
            {
                Id = x.Id,
                Name = x.Name,
                City = x.City,
                Country = x.Country,
                DescriptionHtml = x.DescriptionHtml,
                IsActive = x.IsActive,
                SubscriptionPriceOverride = x.SubscriptionPriceOverride,
                SchoolLeaderCommissionPercentOverride = x.SchoolLeaderCommissionPercentOverride,
                TeacherCommissionPercentOverride = x.TeacherCommissionPercentOverride,
                ProductOwnerCommissionPercentOverride = x.ProductOwnerCommissionPercentOverride
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<UserFormModel?> GetUserForEditAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .Include(x => x.UserSchools)
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);

        var roleName = roles.FirstOrDefault() ?? Rollen.Schueler;
        var teacherClassAssignments = roleName == Rollen.Lehrer
            ? await dbContext.LehrerNiveauZuordnungen
                .AsNoTracking()
                .Where(assignment => assignment.LehrerID == user.Id)
                .Select(assignment => new TeacherClassAssignmentFormModel
                {
                    SchoolId = assignment.SchulID,
                    KlasseId = assignment.KlasseId,
                    Wochenstunden = assignment.Wochenstunden
                })
                .ToListAsync(cancellationToken)
            : [];

        return new UserFormModel
        {
            UserId = user.Id,
            Vorname = user.Vorname,
            Nachname = user.Nachname,
            Email = user.Email ?? string.Empty,
            TelefonNummer = user.TelefonNummer,
            RoleName = roleName,
            IsBlocked = user.IsBlocked,
            IsSelfRegistered = user.IsSelfRegistered,
            SchoolIds = user.UserSchools.Select(x => x.SchoolId).ToList(),
            CurrentKlasseId = await dbContext.StudentProfiles
                .Where(profile => profile.AppUserId == user.Id)
                .Select(profile => profile.CurrentKlasseId)
                .FirstOrDefaultAsync(cancellationToken),
            ElternTelefonNummer = await dbContext.StudentProfiles
                .Where(profile => profile.AppUserId == user.Id)
                .Select(profile => profile.ElternTelefonNummer)
                .FirstOrDefaultAsync(cancellationToken),
            ElternEmail = await dbContext.StudentProfiles
                .Where(profile => profile.AppUserId == user.Id)
                .Select(profile => profile.ElternEmail)
                .FirstOrDefaultAsync(cancellationToken),
            TeacherClassAssignments = teacherClassAssignments
        };
    }

    public async Task<(bool Succeeded, string Message)> SaveUserAsync(UserFormModel model, CancellationToken cancellationToken = default, IReadOnlyCollection<Guid>? managedSchoolIds = null)
    {
        model.SchoolIds = model.SchoolIds.Where(schoolId => schoolId != Guid.Empty).Distinct().ToList();
        if (model.RoleName == Rollen.Schueler && model.SchoolIds.Count > 1)
        {
            return (false, "Un élève ne peut être attribué qu'à une seule école.");
        }

        if (model.RoleName == Rollen.Schueler && model.CurrentKlasseId is Guid klasseId)
        {
            var belongsToStudentSchool = await dbContext.Klassen.AnyAsync(klasse => klasse.Id == klasseId && klasse.SchoolId == model.SchoolIds.FirstOrDefault(), cancellationToken);
            if (!belongsToStudentSchool)
            {
                return (false, "Veuillez choisir une classe de l'école sélectionnée.");
            }
        }

        var normalizedEmail = model.Email.Trim();
        var duplicateExists = await dbContext.Users.AnyAsync(
            x => x.Email == normalizedEmail && (!model.UserId.HasValue || x.Id != model.UserId.Value),
            cancellationToken);

        if (duplicateExists)
        {
            return (false, "Un utilisateur avec cette adresse e-mail existe déjà.");
        }

        AppUser user;
        if (model.UserId.HasValue)
        {
            user = await userManager.FindByIdAsync(model.UserId.Value.ToString()) ?? new AppUser();
            if (user.Id == Guid.Empty)
            {
                return (false, "L'utilisateur est introuvable.");
            }
        }
        else
        {
            user = new AppUser
            {
                UserName = normalizedEmail,
                Email = normalizedEmail,
                EmailConfirmed = true
            };

            var password = string.IsNullOrWhiteSpace(model.Password) ? "MotDePasse1" : model.Password;
            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                return (false, string.Join(" ", createResult.Errors.Select(x => x.Description)));
            }
        }

        user.Vorname = model.Vorname.Trim();
        user.Nachname = model.Nachname.Trim();
        user.FullName = model.FullName;
        user.Email = normalizedEmail;
        user.UserName = normalizedEmail;
        user.TelefonNummer = NormalizeOptional(model.TelefonNummer);
        user.IsBlocked = model.IsBlocked;
        user.IsSelfRegistered = model.IsSelfRegistered;
        user.UpdatedAtUtc = DateTime.UtcNow;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return (false, string.Join(" ", updateResult.Errors.Select(x => x.Description)));
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        if (currentRoles.Count > 0)
        {
            await userManager.RemoveFromRolesAsync(user, currentRoles);
        }

        await userManager.AddToRoleAsync(user, model.RoleName);

        await SyncUserSchoolsAsync(user.Id, model.SchoolIds, model.RoleName is Rollen.Lehrer or Rollen.SchulLeiter or Rollen.Koordinator, cancellationToken, managedSchoolIds);
        await EnsureStudentProfileStateAsync(user, model.RoleName, model.SchoolIds, model.CurrentKlasseId, model.ElternTelefonNummer, model.ElternEmail, cancellationToken);
        if (model.RoleName == Rollen.Lehrer)
        {
            await SyncTeacherClassAssignmentsAsync(user.Id, model.SchoolIds, model.TeacherClassAssignments, cancellationToken, managedSchoolIds);
        }

        return (true, model.UserId.HasValue ? "L'utilisateur a été mis à jour." : "L'utilisateur a été créé.");
    }

    private async Task SyncTeacherClassAssignmentsAsync(
        Guid teacherId,
        IReadOnlyCollection<Guid> schoolIds,
        IReadOnlyCollection<TeacherClassAssignmentFormModel> assignments,
        CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? managedSchoolIds = null)
    {
        var existingQuery = dbContext.LehrerNiveauZuordnungen.Where(assignment => assignment.LehrerID == teacherId);
        if (managedSchoolIds is not null)
        {
            existingQuery = existingQuery.Where(assignment => managedSchoolIds.Contains(assignment.SchulID));
        }

        var existing = await existingQuery.ToListAsync(cancellationToken);
        dbContext.LehrerNiveauZuordnungen.RemoveRange(existing);

        var validAssignments = assignments
            .Where(assignment => schoolIds.Contains(assignment.SchoolId)
                && (managedSchoolIds is null || managedSchoolIds.Contains(assignment.SchoolId))
                && assignment.KlasseId != Guid.Empty)
            .DistinctBy(assignment => new { assignment.SchoolId, assignment.KlasseId })
            .ToList();
        var validClassKeys = await dbContext.Klassen
            .AsNoTracking()
            .Where(klasse => validAssignments.Select(assignment => assignment.KlasseId).Contains(klasse.Id))
            .Select(klasse => new { klasse.Id, klasse.SchoolId, klasse.IstSystemNiveau })
            .ToListAsync(cancellationToken);

        foreach (var assignment in validAssignments.Where(assignment => validClassKeys.Any(klasse => klasse.Id == assignment.KlasseId && (klasse.SchoolId == assignment.SchoolId || (klasse.SchoolId is null && klasse.IstSystemNiveau)))))
        {
            dbContext.LehrerNiveauZuordnungen.Add(new LehrerNiveauZuordnung
            {
                LehrerID = teacherId,
                SchulID = assignment.SchoolId,
                KlasseId = assignment.KlasseId,
                Wochenstunden = Math.Max(1m, assignment.Wochenstunden)
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Entfernt ausschließlich in der Development-Umgebung einen Schulleiter,
    /// alle ihm zugeordneten Schulen und deren Testdaten endgültig. Benutzer,
    /// die noch einer nicht betroffenen Schule zugeordnet sind, bleiben erhalten.
    /// </summary>
    public async Task<(bool Succeeded, string Message)> HardDeleteSchoolLeaderForDevelopmentAsync(
        Guid schoolLeaderId,
        CancellationToken cancellationToken = default)
    {
        if (hostEnvironment?.IsDevelopment() != true)
        {
            return (false, "La suppression définitive des données de test est disponible uniquement en environnement Development.");
        }

        var schoolLeader = await userManager.FindByIdAsync(schoolLeaderId.ToString());
        if (schoolLeader is null || !(await userManager.IsInRoleAsync(schoolLeader, "SchulLeiter")))
        {
            return (false, "Le directeur sélectionné est introuvable.");
        }

        var schoolIds = await dbContext.UserSchools
            .Where(link => link.AppUserId == schoolLeaderId)
            .Select(link => link.SchoolId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (schoolIds.Count == 0)
        {
            return (false, "Ce directeur n'est affecté à aucune école.");
        }

        var classIds = await dbContext.Klassen
            .Where(klasse => klasse.SchoolId.HasValue && schoolIds.Contains(klasse.SchoolId.Value))
            .Select(klasse => klasse.Id)
            .ToListAsync(cancellationToken);

        var assignedUserIds = await dbContext.UserSchools
            .Where(link => schoolIds.Contains(link.SchoolId))
            .Select(link => link.AppUserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var studentUserIds = await dbContext.StudentProfiles
            .Where(profile => schoolIds.Contains(profile.SchoolId))
            .Select(profile => profile.AppUserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var targetUserIds = assignedUserIds
            .Concat(studentUserIds)
            .Append(schoolLeaderId)
            .Distinct()
            .ToList();

        var retainedUserIds = await dbContext.UserSchools
            .Where(link => targetUserIds.Contains(link.AppUserId) && !schoolIds.Contains(link.SchoolId))
            .Select(link => link.AppUserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var usersWithOtherStudentProfiles = await dbContext.StudentProfiles
            .Where(profile => targetUserIds.Contains(profile.AppUserId) && !schoolIds.Contains(profile.SchoolId))
            .Select(profile => profile.AppUserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        retainedUserIds = retainedUserIds
            .Concat(usersWithOtherStudentProfiles)
            .Distinct()
            .ToList();
        var usersToDelete = targetUserIds.Except(retainedUserIds).ToList();

        // Bei aktivierter Retry-Logik (Azure SQL) muss eine benutzerdefinierte
        // Transaktion vollständig innerhalb der Execution Strategy ablaufen,
        // damit ein Wiederholungsversuch den gesamten Block erneut ausführt.
        var strategy = dbContext.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(LoescheAsync);
        }
        catch (Exception exception)
        {
            // Eine nicht committete Transaktion wird beim Dispose automatisch zurückgerollt.
            return (false, $"La suppression définitive a échoué : {exception.Message}");
        }

        async Task<(bool Succeeded, string Message)> LoescheAsync()
        {
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;

            var studentIds = await dbContext.StudentProfiles
                .Where(profile => schoolIds.Contains(profile.SchoolId))
                .Select(profile => profile.AppUserId)
                .ToListAsync(cancellationToken);

            var runIds = await dbContext.KursNiveauDurchlaeufe
                .Where(run => schoolIds.Contains(run.SchulID))
                .Select(run => run.Id)
                .ToListAsync(cancellationToken);
            dbContext.RatenZahlungen.RemoveRange(await dbContext.RatenZahlungen
                .Where(payment => runIds.Contains(payment.DurchlaufID) || studentIds.Contains(payment.StudentId))
                .ToListAsync(cancellationToken));

            dbContext.FinanzBuchungen.RemoveRange(await dbContext.FinanzBuchungen
                .Where(entry => schoolIds.Contains(entry.SchulID))
                .ToListAsync(cancellationToken));
            dbContext.FinanzKategorien.RemoveRange(await dbContext.FinanzKategorien
                .Where(category => category.SchulID.HasValue && schoolIds.Contains(category.SchulID.Value))
                .ToListAsync(cancellationToken));
            dbContext.KursNiveauDurchlaeufe.RemoveRange(await dbContext.KursNiveauDurchlaeufe
                .Where(run => schoolIds.Contains(run.SchulID))
                .ToListAsync(cancellationToken));

            dbContext.LehrerNiveauZuordnungen.RemoveRange(await dbContext.LehrerNiveauZuordnungen
                .Where(assignment => schoolIds.Contains(assignment.SchulID))
                .ToListAsync(cancellationToken));
            dbContext.UserSchools.RemoveRange(await dbContext.UserSchools
                .Where(link => schoolIds.Contains(link.SchoolId))
                .ToListAsync(cancellationToken));

            dbContext.ExamAttempts.RemoveRange(await dbContext.ExamAttempts
                .Where(attempt => studentIds.Contains(attempt.StudentId))
                .ToListAsync(cancellationToken));
            dbContext.ExerciseAttempts.RemoveRange(await dbContext.ExerciseAttempts
                .Where(attempt => studentIds.Contains(attempt.StudentId))
                .ToListAsync(cancellationToken));
            dbContext.LessonProgresses.RemoveRange(await dbContext.LessonProgresses
                .Where(progress => studentIds.Contains(progress.StudentId))
                .ToListAsync(cancellationToken));
            dbContext.LevelEnrollments.RemoveRange(await dbContext.LevelEnrollments
                .Where(enrollment => studentIds.Contains(enrollment.StudentId) || classIds.Contains(enrollment.KlasseId))
                .ToListAsync(cancellationToken));
            dbContext.StudentProfiles.RemoveRange(await dbContext.StudentProfiles
                .Where(profile => schoolIds.Contains(profile.SchoolId))
                .ToListAsync(cancellationToken));

            var teacherLessonIds = await dbContext.TeacherLessons
                .Where(lesson => classIds.Contains(lesson.KlasseId) || (lesson.AuthorId.HasValue && usersToDelete.Contains(lesson.AuthorId.Value)))
                .Select(lesson => lesson.Id)
                .ToListAsync(cancellationToken);
            dbContext.TeacherLessons.RemoveRange(await dbContext.TeacherLessons
                .Where(lesson => teacherLessonIds.Contains(lesson.Id))
                .ToListAsync(cancellationToken));
            dbContext.Klassen.RemoveRange(await dbContext.Klassen
                .Where(klasse => classIds.Contains(klasse.Id))
                .ToListAsync(cancellationToken));
            dbContext.Abrechnungsperioden.RemoveRange(await dbContext.Abrechnungsperioden
                .Where(period => schoolIds.Contains(period.SchulID))
                .ToListAsync(cancellationToken));

            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var userId in usersToDelete)
            {
                var user = await userManager.FindByIdAsync(userId.ToString());
                if (user is not null)
                {
                    var roles = await userManager.GetRolesAsync(user);
                    if (roles.Count > 0)
                    {
                        var removeRolesResult = await userManager.RemoveFromRolesAsync(user, roles);
                        if (!removeRolesResult.Succeeded)
                        {
                            throw new InvalidOperationException(string.Join(" ", removeRolesResult.Errors.Select(error => error.Description)));
                        }
                    }

                    var deleteResult = await userManager.DeleteAsync(user);
                    if (!deleteResult.Succeeded)
                    {
                        throw new InvalidOperationException(string.Join(" ", deleteResult.Errors.Select(error => error.Description)));
                    }
                }
            }

            dbContext.Schools.RemoveRange(await dbContext.Schools
                .Where(school => schoolIds.Contains(school.Id))
                .ToListAsync(cancellationToken));
            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return (true, "Le directeur, ses écoles et toutes les données de test associées ont été supprimés définitivement.");
        }
    }

    public async Task<CourseFormModel?> GetCourseForEditAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Klassen
            .AsNoTracking()
            .Where(x => x.Id == courseId)
            .Select(x => new CourseFormModel
            {
                CourseId = x.Id,
                Code = x.Code,
                Title = x.Title,
                CEFRGroup = x.CEFRGroup,
                Description = x.Description,
                RecommendedLessonCount = x.RecommendedLessonCount,
                Price = x.Price,
                MaxPlaetze = x.MaxPlaetze,
                SchoolId = x.SchoolId,
                KlassenStufeId = x.KlassenStufeId,
                ReferenceName = x.KlassenStufe != null ? x.KlassenStufe.Name : null,
                IstSystemNiveau = x.IstSystemNiveau,
                AutoAssignSkoulouAppAboOnCycleStart = x.AutoAssignSkoulouAppAboOnCycleStart
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(bool Succeeded, string Message, Guid? CourseId)> SaveCourseAsync(CourseFormModel model, CancellationToken cancellationToken = default)
    {
        var normalizedCode = model.Code.Trim();
        // Klassen-Code ist pro Schule eindeutig (globale System-Niveaus teilen sich SchoolId == null).
        var duplicateExists = await dbContext.Klassen.AnyAsync(
            x => x.Code == normalizedCode
                 && x.SchoolId == model.SchoolId
                 && (!model.CourseId.HasValue || x.Id != model.CourseId.Value),
            cancellationToken);

        if (duplicateExists)
        {
            return (false, "Un cours avec ce code existe déjà.", null);
        }

        Klasse course;
        if (model.CourseId.HasValue)
        {
            course = await dbContext.Klassen.FirstOrDefaultAsync(x => x.Id == model.CourseId.Value, cancellationToken) ?? new Klasse();
            if (course.Id == Guid.Empty)
            {
                return (false, "Le cours est introuvable.", null);
            }

            // Feste Sprachschul-Niveaus (A1–C) dürfen nicht bearbeitet werden.
            if (course.IstSystemNiveau)
            {
                return (false, "Ce niveau standard ne peut pas être modifié.", null);
            }
        }
        else
        {
            course = new Klasse
            {
                // Neu angelegte Klassen sind immer schul-eigene Klassen, niemals System-Niveaus.
                SchoolId = model.SchoolId,
                IstSystemNiveau = false
            };
            await dbContext.Klassen.AddAsync(course, cancellationToken);
        }

        course.Code = normalizedCode;
        course.Title = model.Title.Trim();
        course.CEFRGroup = model.CEFRGroup.Trim();
        course.Description = model.Description.Trim();
        course.RecommendedLessonCount = model.RecommendedLessonCount;
        course.Price = model.Price;
        course.MaxPlaetze = model.MaxPlaetze;
        course.GeaendertAmUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, model.CourseId.HasValue ? "Le cours a été mis à jour." : "Le cours a été créé.", course.Id);
    }

    /// <summary>Liefert die Referenzklassen/-stufen einer Schule samt Verwendungsanzahl.</summary>
    public async Task<IReadOnlyList<KlassenStufeViewModel>> GetKlassenStufenAsync(Guid schoolId, CancellationToken cancellationToken = default)
    {
        return await dbContext.KlassenStufen
            .AsNoTracking()
            .Where(stufe => stufe.SchoolId == schoolId)
            .OrderBy(stufe => stufe.Name)
            .Select(stufe => new KlassenStufeViewModel
            {
                Id = stufe.Id,
                Name = stufe.Name,
                KlassenCount = stufe.Klassen.Count
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>Löscht eine Klassenstufe ausschließlich, wenn keine Klasse darauf verweist.</summary>
    public async Task<(bool Succeeded, string Message)> DeleteKlassenStufeAsync(Guid schoolId, Guid klassenStufeId, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return (false, "Les directeurs peuvent modifier les classes scolaires, mais pas les supprimer.");
    }

    /// <summary>
    /// Liefert ausschließlich die von einer Schule selbst verwalteten Klassen.
    /// Globale System-Niveaus dienen nur als Grundlage für Scorebewertungen und
    /// werden niemals in der Schulleiter-Verwaltung ausgegeben.
    /// </summary>
    public async Task<IReadOnlyList<CourseFormModel>> GetSchoolKlassenAsync(Guid schoolId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Klassen
            .AsNoTracking()
            .Where(x => x.SchoolId == schoolId && !x.IstSystemNiveau)
            .OrderBy(x => x.KlassenStufe != null ? x.KlassenStufe.Name : string.Empty)
            .ThenBy(x => x.Title)
            .ThenBy(x => x.Code)
            .Select(x => new CourseFormModel
            {
                CourseId = x.Id,
                Code = x.Code,
                Title = x.Title,
                CEFRGroup = x.CEFRGroup,
                Description = x.Description,
                RecommendedLessonCount = x.RecommendedLessonCount,
                Price = x.Price,
                MaxPlaetze = x.MaxPlaetze,
                CurrentStudentCount = dbContext.StudentProfiles.Count(profile => profile.CurrentKlasseId == x.Id && !profile.IsArchived),
                SchoolId = x.SchoolId,
                KlassenStufeId = x.KlassenStufeId,
                ReferenceName = x.KlassenStufe != null ? x.KlassenStufe.Name : null,
                IstSystemNiveau = x.IstSystemNiveau,
                AutoAssignSkoulouAppAboOnCycleStart = x.AutoAssignSkoulouAppAboOnCycleStart
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Speichert eine schul-eigene Klasse für den Schulleiter. System-Niveaus werden hier niemals verändert.
    /// </summary>
    public async Task<(bool Succeeded, string Message, Guid? CourseId)> SaveSchoolKlasseForSchoolLeaderAsync(
        Guid schoolLeaderId,
        Guid schoolId,
        CourseFormModel model,
        CancellationToken cancellationToken = default)
    {
        var assignedSchoolIds = await dbContext.UserSchools
            .AsNoTracking()
            .Where(link => link.AppUserId == schoolLeaderId && link.CanManageStudents)
            .Select(link => link.SchoolId)
            .ToListAsync(cancellationToken);
        var isAssigned = assignedSchoolIds.Contains(schoolId);
        if (!isAssigned)
        {
            return (false, "Tu ne peux gérer que tes propres écoles.", null);
        }

        return await SaveSchoolKlasseAsync(schoolId, model, schoolLeaderId, cancellationToken);
    }

    /// <summary>
    /// Speichert eine schul-eigene Klasse. Diese Methode dient internen Verwaltungsabläufen;
    /// Schulleiter verwenden <see cref="SaveSchoolKlasseForSchoolLeaderAsync"/>.
    /// </summary>
    public async Task<(bool Succeeded, string Message, Guid? CourseId)> SaveSchoolKlasseAsync(
        Guid schoolId,
        CourseFormModel model,
        Guid erfasstVonUserId = default,
        CancellationToken cancellationToken = default)
    {
        var school = await dbContext.Schools.FirstOrDefaultAsync(x => x.Id == schoolId, cancellationToken);
        if (school is null)
        {
            return (false, "L'école est introuvable.", null);
        }

        var normalizedClassName = model.Title.Trim();
        if (string.IsNullOrWhiteSpace(normalizedClassName))
        {
            return (false, "Veuillez saisir le nom de la classe.", null);
        }

        var normalizedCode = normalizedClassName;

        var duplicateExists = await dbContext.Klassen.AnyAsync(
            x => x.Code == normalizedCode
                 && x.SchoolId == schoolId
                 && (!model.CourseId.HasValue || x.Id != model.CourseId.Value),
            cancellationToken);

        if (duplicateExists)
        {
            return (false, "Une classe avec ce nom existe déjà dans ton école.", null);
        }

        Klasse klasse;
        var warAutoAssignAktiv = false;
        if (model.CourseId.HasValue)
        {
            klasse = await dbContext.Klassen.FirstOrDefaultAsync(
                x => x.Id == model.CourseId.Value && x.SchoolId == schoolId, cancellationToken)
                ?? new Klasse();
            if (klasse.Id == Guid.Empty)
            {
                return (false, "La classe est introuvable pour cette école.", null);
            }
            if (klasse.IstSystemNiveau)
            {
                return (false, "Ce niveau standard ne peut pas être modifié.", null);
            }

            warAutoAssignAktiv = klasse.AutoAssignSkoulouAppAboOnCycleStart;
        }
        else
        {
            klasse = new Klasse
            {
                SchoolId = schoolId,
                IstSystemNiveau = false
            };
            await dbContext.Klassen.AddAsync(klasse, cancellationToken);
        }

        var autoAssignWirdAktiviert = model.CourseId.HasValue
            && !warAutoAssignAktiv
            && model.AutoAssignSkoulouAppAboOnCycleStart;
        var autoAssignWirdDeaktiviert = model.CourseId.HasValue
            && warAutoAssignAktiv
            && !model.AutoAssignSkoulouAppAboOnCycleStart;

        var hatAktivenCycle = false;
        if ((autoAssignWirdAktiviert || autoAssignWirdDeaktiviert) && klasse.Id != Guid.Empty)
        {
            hatAktivenCycle = await dbContext.KursNiveauDurchlaeufe
                .AsNoTracking()
                .AnyAsync(durchlauf => durchlauf.SchulID == schoolId
                    && durchlauf.KlasseId == klasse.Id
                    && durchlauf.EndDatum == null,
                    cancellationToken);
        }

        if (autoAssignWirdAktiviert && hatAktivenCycle && !model.ConfirmAutoAssignActivationForActiveCycle)
        {
            return (false, "Confirmation requise : l'activation automatique dans un cycle actif attribue immédiatement l'abonnement aux élèves actuels de la classe.", null);
        }

        if (autoAssignWirdDeaktiviert && hatAktivenCycle)
        {
            var hasStudentsWithAboInCurrentClass = await (from profile in dbContext.StudentProfiles.AsNoTracking()
                                                          join enrollment in dbContext.LevelEnrollments.AsNoTracking()
                                                              on new { StudentId = profile.AppUserId, KlasseId = klasse.Id }
                                                              equals new { enrollment.StudentId, enrollment.KlasseId }
                                                          where profile.SchoolId == schoolId
                                                                && profile.CurrentKlasseId == klasse.Id
                                                                && enrollment.IstAktuell
                                                                && (enrollment.IsUnlocked || enrollment.IsPaid)
                                                          select enrollment.Id)
                .AnyAsync(cancellationToken);

            if (hasStudentsWithAboInCurrentClass)
            {
                return (false, "Impossible de désactiver l'option : des élèves de la classe active ont déjà un abonnement. Tu pourras la modifier au prochain cycle.", null);
            }
        }

        KlassenStufe? klassenStufe = null;
        if (!string.IsNullOrWhiteSpace(model.NeueKlassenStufe))
        {
            var neuerName = model.NeueKlassenStufe.Trim();
            klassenStufe = await dbContext.KlassenStufen
                .FirstOrDefaultAsync(item => item.SchoolId == schoolId && item.Name == neuerName, cancellationToken);
            if (klassenStufe is null)
            {
                klassenStufe = new KlassenStufe { SchoolId = schoolId, Name = neuerName };
                await dbContext.KlassenStufen.AddAsync(klassenStufe, cancellationToken);
            }
        }
        else if (model.KlassenStufeId.HasValue)
        {
            klassenStufe = await dbContext.KlassenStufen
                .FirstOrDefaultAsync(item => item.Id == model.KlassenStufeId.Value && item.SchoolId == schoolId, cancellationToken)
                ?? throw new InvalidOperationException("La référence de classe sélectionnée est introuvable.");
        }
        else
        {
            return (false, "Sélectionne ou crée un nom de référence pour cette classe.", null);
        }

        klasse.Code = normalizedCode;
        klasse.Title = normalizedClassName;
        klasse.CEFRGroup = string.IsNullOrWhiteSpace(model.CEFRGroup) ? "Classe" : model.CEFRGroup.Trim();
        klasse.KlassenStufeId = klassenStufe?.Id;
        klasse.Description = string.IsNullOrWhiteSpace(model.Description) ? string.Empty : model.Description.Trim();
        klasse.RecommendedLessonCount = model.RecommendedLessonCount;
        klasse.Price = model.Price;
        klasse.MaxPlaetze = model.MaxPlaetze;
        klasse.AutoAssignSkoulouAppAboOnCycleStart = model.AutoAssignSkoulouAppAboOnCycleStart;

        await dbContext.SaveChangesAsync(cancellationToken);

        if (autoAssignWirdAktiviert && hatAktivenCycle)
        {
            var studentIds = await dbContext.StudentProfiles
                .AsNoTracking()
                .Where(profile => profile.SchoolId == schoolId && profile.CurrentKlasseId == klasse.Id)
                .Select(profile => profile.AppUserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (studentIds.Count > 0)
            {
                var aboService = new AboService(dbContext);
                foreach (var studentId in studentIds)
                {
                    var (succeeded, errorMessage) = await aboService.AboZuweisenAsync(
                        studentId,
                        klasse.Id,
                        AboTyp.SkoulouApp,
                        erfasstVonUserId: erfasstVonUserId,
                        cancellationToken: cancellationToken);
                    if (!succeeded)
                    {
                        return (false, errorMessage, klasse.Id);
                    }
                }
            }

            return (true, $"La classe a été mise à jour. {studentIds.Count} élève(s) actif(s) ont reçu l'abonnement automatiquement.", klasse.Id);
        }

        return (true, model.CourseId.HasValue ? "La classe a été mise à jour." : "La classe a été créée.", klasse.Id);
    }

    /// <summary>
    /// Löscht eine schul-eigene Klasse. System-Niveaus können nicht gelöscht werden.
    /// </summary>
    public async Task<(bool Succeeded, string Message)> DeleteSchoolKlasseAsync(Guid schoolId, Guid klasseId, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return (false, "Les directeurs peuvent modifier les classes, mais pas les supprimer.");
    }

    public async Task<LessonFormModel?> GetLessonForEditAsync(Guid lessonId, CancellationToken cancellationToken = default)
    {
        var lesson = await dbContext.Lessons
            .AsNoTracking()
            .Include(x => x.Exercises)
                .ThenInclude(x => x.Options)
            .Include(x => x.Fach)
            .Include(x => x.MediaItems)
            .FirstOrDefaultAsync(x => x.Id == lessonId, cancellationToken);

        if (lesson is null)
        {
            return null;
        }

        var lessonExerciseIds = lesson.Exercises.Select(exercise => exercise.Id).ToList();
        var hasSubmissionsInCurrentAssignmentVersion = lessonExerciseIds.Count > 0 && await dbContext.ExerciseAttempts
            .AsNoTracking()
            .AnyAsync(attempt => attempt.AssignmentVersion == lesson.AssignmentVersion
                && lessonExerciseIds.Contains(attempt.ExerciseId), cancellationToken);

        var dueStillRunning = lesson.ExerciseSubmissionDueAtUtc.HasValue
            && DateTime.UtcNow <= lesson.ExerciseSubmissionDueAtUtc.Value;
        var isCurrentAssignmentVersionLocked = hasSubmissionsInCurrentAssignmentVersion && dueStillRunning;

        return new LessonFormModel
        {
            LessonId = lesson.Id,
            CourseId = lesson.KlasseId,
            AssignmentVersion = Math.Max(1, lesson.AssignmentVersion),
            HasSubmissionsInCurrentAssignmentVersion = hasSubmissionsInCurrentAssignmentVersion,
            IsCurrentAssignmentVersionLocked = isCurrentAssignmentVersionLocked,
            ExerciseSubmissionDueAtUtc = lesson.ExerciseSubmissionDueAtUtc,
            FachId = lesson.FachId,
            FachName = lesson.Fach != null ? lesson.Fach.Name : null,
            Title = lesson.Title,
            Summary = lesson.Summary,
            Content = lesson.Content,
            VideoAssetId = TryParseLessonVideoAssetId(lesson),
            VideoDisplayName = await GetLessonVideoDisplayNameAsync(lesson, cancellationToken),
            VideoDuration = await GetLessonVideoDurationAsync(lesson, cancellationToken),
            VideoUrl = BuildLessonVideoUrl(lesson),
            Exercises = lesson.Exercises
                .OrderBy(x => x.Order)
                .Select(exercise =>
                {
                    var normalizedType = NormalizeLessonExerciseType(exercise.ExerciseType);
                    return new LessonExerciseFormModel
                    {
                        ExerciseId = exercise.Id,
                        Title = exercise.Title,
                        Instructions = exercise.Instructions,
                        Question = exercise.Question,
                        Points = exercise.Points > 0 ? exercise.Points : 10,
                        ExerciseType = normalizedType,
                        Order = exercise.Order,
                        AudioText = exercise.AudioText,
                        TtsLanguage = NormalizeTtsLanguage(exercise.TtsLanguage),
                        AcceptedAnswers = string.Join('\n', AnswerMatchingService.ParseAcceptedAnswers(exercise.AcceptedAnswersJson)),
                        Gaps = AnswerMatchingService.ParseGapAnswers(exercise.GapAnswersJson)
                            .Select(gap => new LessonExerciseGapFormModel
                            {
                                AcceptedAnswers = string.Join('\n', gap)
                            })
                            .ToList(),
                        Options = exercise.Options
                            .OrderBy(x => x.Order)
                            .Select(option => new LessonExerciseOptionFormModel
                            {
                                OptionId = option.Id,
                                Text = option.Text,
                                IsCorrect = option.IsCorrect
                            })
                            .ToList()
                    };
                })
                .ToList()
        };
    }

    public async Task<(bool Succeeded, string Message)> SaveLessonAsync(
        LessonFormModel model,
        Guid? currentTeacherId = null,
        CancellationToken cancellationToken = default)
    {
        var courseExists = await dbContext.Klassen.AnyAsync(x => x.Id == model.CourseId, cancellationToken);
        if (!courseExists)
        {
            return (false, "Le cours associé est introuvable.");
        }

        Lesson lesson;
        if (model.LessonId.HasValue)
        {
            lesson = await dbContext.Lessons
                .Include(x => x.Exercises)
                    .ThenInclude(x => x.Options)
                .Include(x => x.MediaItems)
                .FirstOrDefaultAsync(x => x.Id == model.LessonId.Value, cancellationToken) ?? new Lesson();
            if (lesson.Id == Guid.Empty)
            {
                return (false, "La leçon est introuvable.");
            }

            var lessonExerciseIds = lesson.Exercises.Select(ex => ex.Id).ToList();
            var hasAttemptsInCurrentVersion = lessonExerciseIds.Count > 0 && await dbContext.ExerciseAttempts
                .AsNoTracking()
                .AnyAsync(attempt => attempt.AssignmentVersion == lesson.AssignmentVersion
                    && lessonExerciseIds.Contains(attempt.ExerciseId), cancellationToken);

            var nowUtc = DateTime.UtcNow;
            var dueAtUtc = lesson.ExerciseSubmissionDueAtUtc;
            var dueStillRunning = dueAtUtc.HasValue && nowUtc <= dueAtUtc.Value;

            if (hasAttemptsInCurrentVersion && dueStillRunning)
            {
                return (false, "Cette leçon a déjà des réponses d'élèves pour la session en cours. Les modifications seront possibles après la date limite ou via une nouvelle session.");
            }

            if (hasAttemptsInCurrentVersion && (!dueAtUtc.HasValue || nowUtc > dueAtUtc.Value))
            {
                lesson.AssignmentVersion = Math.Max(1, lesson.AssignmentVersion) + 1;
            }
        }
        else
        {
            lesson = new Lesson();
            await dbContext.Lessons.AddAsync(lesson, cancellationToken);
        }

        lesson.KlasseId = model.CourseId;
        lesson.FachId = await ResolveOrCreateFachIdForCourseAsync(model.CourseId, model.FachId, model.NeueFachBezeichnung, cancellationToken);
        lesson.Title = model.Title.Trim();
        lesson.Summary = model.Summary.Trim();
        lesson.Content = model.Content.Trim();
        if (!model.LessonId.HasValue)
        {
            var nextOrder = await dbContext.Lessons
                .AsNoTracking()
                .Where(existingLesson => existingLesson.KlasseId == model.CourseId)
                .MaxAsync(existingLesson => (int?)existingLesson.Order, cancellationToken) ?? 0;
            lesson.Order = nextOrder + 1;
        }
        lesson.IsPublished = model.IsPublished;
        lesson.ExerciseSubmissionDueAtUtc = model.ExerciseSubmissionDueAtUtc;

        if (!model.LessonId.HasValue && currentTeacherId.HasValue)
        {
            lesson.CreatedByTeacherId = currentTeacherId.Value;
        }

        if (model.LessonId.HasValue && !lesson.CreatedByTeacherId.HasValue && currentTeacherId.HasValue)
        {
            lesson.CreatedByTeacherId = currentTeacherId.Value;
        }

        lesson.GeaendertAmUtc = DateTime.UtcNow;

        await SyncLessonVideoMediaAsync(lesson, model, cancellationToken);

        SyncLessonExercises(lesson, model.Exercises);

        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, model.LessonId.HasValue ? "La leçon a été mise à jour." : "La leçon a été créée.");
    }

    public async Task<(bool Succeeded, string Message)> ChangeLessonSubmissionDueAsync(
        Guid lessonId, DateTime? dueAtUtc, Guid teacherId, CancellationToken cancellationToken = default)
    {
        var lesson = await dbContext.Lessons
            .Include(x => x.Exercises)
            .FirstOrDefaultAsync(x => x.Id == lessonId, cancellationToken);
        if (lesson is null || (lesson.CreatedByTeacherId != teacherId
            && (lesson.CreatedByTeacherId.HasValue || !await dbContext.LehrerNiveauZuordnungen.AsNoTracking()
                .AnyAsync(x => x.LehrerID == teacherId && x.KlasseId == lesson.KlasseId, cancellationToken))))
        {
            return (false, "Cette leçon n'est pas autorisée pour votre profil enseignant.");
        }

        if (dueAtUtc.HasValue && dueAtUtc.Value <= DateTime.UtcNow)
        {
            return (false, "Choisissez une date limite future.");
        }

        var previousDueAtUtc = lesson.ExerciseSubmissionDueAtUtc;
        if (dueAtUtc.HasValue && (!previousDueAtUtc.HasValue || dueAtUtc.Value > previousDueAtUtc.Value))
        {
            var exerciseIds = lesson.Exercises.Select(x => x.Id).ToList();
            if (exerciseIds.Count > 0 && await dbContext.ExerciseAttempts.AsNoTracking()
                .AnyAsync(a => exerciseIds.Contains(a.ExerciseId) && a.AssignmentVersion == lesson.AssignmentVersion, cancellationToken))
            {
                lesson.AssignmentVersion = Math.Max(1, lesson.AssignmentVersion) + 1;
            }
        }

        lesson.ExerciseSubmissionDueAtUtc = dueAtUtc;
        lesson.GeaendertAmUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, "La date limite a été mise à jour. Tous les élèves peuvent envoyer de nouvelles réponses jusqu'à cette date.");
    }

    private async Task<Guid?> ResolveOrCreateFachIdForCourseAsync(
        Guid courseId,
        Guid? fachId,
        string? neueFachBezeichnung,
        CancellationToken cancellationToken)
    {
        var schoolId = await dbContext.Klassen
            .AsNoTracking()
            .Where(klasse => klasse.Id == courseId)
            .Select(klasse => klasse.SchoolId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(neueFachBezeichnung))
        {
            if (schoolId is null)
            {
                return null;
            }

            var normalized = neueFachBezeichnung.Trim();
            if (normalized.Length == 0)
            {
                return null;
            }

            var normalizedKey = normalized.ToUpperInvariant();
            var existing = await dbContext.Faecher
                .FirstOrDefaultAsync(
                    fach => fach.SchoolId == schoolId.Value && fach.Name.ToUpper() == normalizedKey,
                    cancellationToken);

            if (existing is not null)
            {
                return existing.Id;
            }

            var neuesFach = new Fach
            {
                SchoolId = schoolId.Value,
                Name = normalized
            };
            await dbContext.Faecher.AddAsync(neuesFach, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return neuesFach.Id;
        }

        if (!fachId.HasValue)
        {
            return null;
        }

        var isValid = schoolId.HasValue
            && await dbContext.Faecher
                .AsNoTracking()
                .AnyAsync(fach => fach.Id == fachId.Value && fach.SchoolId == schoolId.Value, cancellationToken);

        return isValid ? fachId : null;
    }

    private async Task SyncLessonVideoMediaAsync(Lesson lesson, LessonFormModel model, CancellationToken cancellationToken)
    {
        var existingVideoEntries = lesson.MediaItems
            .Where(media => string.Equals(media.MediaType, "video", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!model.VideoAssetId.HasValue)
        {
            if (existingVideoEntries.Count > 0)
            {
                dbContext.LessonMedia.RemoveRange(existingVideoEntries);
            }

            return;
        }

        var assetIdText = model.VideoAssetId.Value.ToString("D");
        var videoAssetExists = await dbContext.BlobMediaAssets
            .AsNoTracking()
            .AnyAsync(asset => asset.Id == model.VideoAssetId.Value, cancellationToken);
        if (!videoAssetExists)
        {
            return;
        }

        var targetPath = $"/media-library/videos/{assetIdText}?lessonId={lesson.Id}";
        if (existingVideoEntries.Any(entry => string.Equals(entry.RelativePath, targetPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (existingVideoEntries.Count > 0)
        {
            dbContext.LessonMedia.RemoveRange(existingVideoEntries);
        }

        lesson.MediaItems.Add(new LessonMedia
        {
            LessonId = lesson.Id,
            MediaType = "video",
            RelativePath = targetPath,
            AltText = model.VideoDisplayName?.Trim() ?? "Vidéo de la leçon",
            StorageProvider = "AzureBlob"
        });
    }

    private static Guid? TryParseLessonVideoAssetId(Lesson lesson)
    {
        var mediaPath = lesson.MediaItems
            .Where(media => string.Equals(media.MediaType, "video", StringComparison.OrdinalIgnoreCase))
            .Select(media => media.RelativePath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            return null;
        }

        var cleanPath = mediaPath;
        var queryIndex = cleanPath.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            cleanPath = cleanPath[..queryIndex];
        }

        var marker = "/media-library/videos/";
        var markerIndex = cleanPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var idText = cleanPath[(markerIndex + marker.Length)..].Trim('/');
        return Guid.TryParse(idText, out var assetId) ? assetId : null;
    }

    private async Task<string?> GetLessonVideoDisplayNameAsync(Lesson lesson, CancellationToken cancellationToken)
    {
        var assetId = TryParseLessonVideoAssetId(lesson);
        if (!assetId.HasValue)
        {
            return null;
        }

        return await dbContext.BlobMediaAssets
            .AsNoTracking()
            .Where(asset => asset.Id == assetId.Value)
            .Select(asset => asset.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<TimeSpan?> GetLessonVideoDurationAsync(Lesson lesson, CancellationToken cancellationToken)
    {
        var assetId = TryParseLessonVideoAssetId(lesson);
        if (!assetId.HasValue)
        {
            return null;
        }

        return await dbContext.BlobMediaAssets
            .AsNoTracking()
            .Where(asset => asset.Id == assetId.Value)
            .Select(asset => asset.VideoDuration)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string? BuildLessonVideoUrl(Lesson lesson)
    {
        var assetId = TryParseLessonVideoAssetId(lesson);
        return assetId.HasValue
            ? $"/media-library/videos/{assetId.Value}?lessonId={lesson.Id}"
            : null;
    }

    private void SyncLessonExercises(Lesson lesson, List<LessonExerciseFormModel> exerciseForms)
    {
        var forms = (exerciseForms ?? [])
            .Select((exercise, index) => new { Exercise = exercise, Order = index + 1 })
            .ToList();

        var existingById = lesson.Exercises.ToDictionary(exercise => exercise.Id);
        var retainedIds = new HashSet<Guid>();

        foreach (var item in forms)
        {
            var form = item.Exercise;
            var normalizedType = NormalizeLessonExerciseType(form.ExerciseType);
            var isChoiceType = IsChoiceType(normalizedType);
            var isSingleChoiceType = IsSingleChoiceType(normalizedType);
            var isAudioType = IsAudioType(normalizedType);

            Exercise exercise;
            if (form.ExerciseId.HasValue && existingById.TryGetValue(form.ExerciseId.Value, out var existing))
            {
                exercise = existing;
            }
            else
            {
                exercise = new Exercise
                {
                    LessonId = lesson.Id
                };
                lesson.Exercises.Add(exercise);
            }

            exercise.Title = form.Title?.Trim() ?? string.Empty;
            exercise.Instructions = form.Instructions?.Trim() ?? string.Empty;
            exercise.Question = form.Question?.Trim() ?? string.Empty;
            exercise.Points = form.Points > 0 ? form.Points : 10;
            exercise.ExerciseType = normalizedType;
            exercise.Order = item.Order;
            exercise.AudioText = isAudioType ? form.AudioText?.Trim() : null;
            exercise.TtsLanguage = NormalizeTtsLanguage(form.TtsLanguage);
            exercise.AcceptedAnswersJson = normalizedType == "GapFill"
                ? null
                : AnswerMatchingService.SerializeVariants(AnswerMatchingService.SplitVariants(form.AcceptedAnswers));
            exercise.GapAnswersJson = normalizedType == "GapFill"
                ? AnswerMatchingService.SerializeGaps((form.Gaps ?? []).Select(g => AnswerMatchingService.SplitVariants(g.AcceptedAnswers)))
                : null;

            if (isChoiceType)
            {
                SyncLessonExerciseOptions(exercise, form.Options ?? [], isSingleChoiceType);
            }
            else if (exercise.Options.Count > 0)
            {
                dbContext.ExerciseOptions.RemoveRange(exercise.Options);
                exercise.Options.Clear();
            }

            retainedIds.Add(exercise.Id);
        }

        var removed = lesson.Exercises
            .Where(exercise => exercise.Id != Guid.Empty && !retainedIds.Contains(exercise.Id))
            .ToList();

        if (removed.Count > 0)
        {
            dbContext.Exercises.RemoveRange(removed);
        }
    }

    private void SyncLessonExerciseOptions(Exercise exercise, List<LessonExerciseOptionFormModel> optionForms, bool singleChoice)
    {
        var orderedOptions = (optionForms ?? [])
            .Where(option => !string.IsNullOrWhiteSpace(option.Text))
            .Select((option, index) => new { Option = option, Order = index + 1 })
            .ToList();

        var existingById = exercise.Options.ToDictionary(option => option.Id);
        var retainedIds = new HashSet<Guid>();
        var hasMarkedCorrect = orderedOptions.Any(option => option.Option.IsCorrect);

        for (var i = 0; i < orderedOptions.Count; i++)
        {
            var formOption = orderedOptions[i].Option;
            ExerciseOption option;
            if (formOption.OptionId.HasValue && existingById.TryGetValue(formOption.OptionId.Value, out var existing))
            {
                option = existing;
            }
            else
            {
                option = new ExerciseOption
                {
                    ExerciseId = exercise.Id
                };
                exercise.Options.Add(option);
            }

            option.Text = formOption.Text.Trim();
            option.Order = orderedOptions[i].Order;
            option.IsCorrect = singleChoice
                ? (hasMarkedCorrect ? formOption.IsCorrect : i == 0)
                : formOption.IsCorrect;

            retainedIds.Add(option.Id);
        }

        var removed = exercise.Options
            .Where(option => option.Id != Guid.Empty && !retainedIds.Contains(option.Id))
            .ToList();
        if (removed.Count > 0)
        {
            dbContext.ExerciseOptions.RemoveRange(removed);
        }
    }

    private static bool IsChoiceType(string normalizedType)
        => normalizedType is "SingleChoice" or "MultipleChoice" or "AudioSingleChoice" or "AudioMultipleChoice";

    private static bool IsSingleChoiceType(string normalizedType)
        => normalizedType is "SingleChoice" or "AudioSingleChoice";

    private static bool IsAudioType(string normalizedType)
        => normalizedType is "AudioText" or "AudioSingleChoice" or "AudioMultipleChoice";

    private static string NormalizeLessonExerciseType(string? exerciseType)
    {
        if (string.IsNullOrWhiteSpace(exerciseType))
        {
            return "SingleChoice";
        }

        return exerciseType.Trim().ToLowerInvariant() switch
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

    private static string NormalizeTtsLanguage(string? language)
        => language switch
        {
            "fr-FR" => "fr-FR",
            "en-US" => "en-US",
            _ => "de-DE"
        };

    public async Task<IReadOnlyList<LessonManagementItem>> GetLessonOptionsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Lessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .OrderBy(x => x.Klasse.Code)
            .ThenBy(x => x.Order)
            .Select(lesson => new LessonManagementItem
            {
                LessonId = lesson.Id,
                Title = $"{lesson.Klasse.Code} · {lesson.Title}",
                Summary = lesson.Summary,
                Order = lesson.Order,
                ExerciseCount = lesson.Exercises.Count
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<(bool Succeeded, string Message)> SaveLessonMediaAsync(MediaUploadFormModel model, Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        if (!model.LessonId.HasValue)
        {
            return (false, "Veuillez choisir une leçon.");
        }

        var lesson = await dbContext.Lessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .FirstOrDefaultAsync(x => x.Id == model.LessonId.Value, cancellationToken);

        if (lesson is null)
        {
            return (false, "La leçon sélectionnée est introuvable.");
        }

        var lessonFolder = $"{lesson.Klasse.Code.ToLowerInvariant()}/lesson-{lesson.Order}";
        var savedMedia = await mediaStorageService.SaveAsync(new MediaSaveRequest
        {
            Content = content,
            FileName = fileName,
            MediaType = model.MediaType,
            AltText = model.AltText,
            LessonFolder = lessonFolder
        }, cancellationToken);

        dbContext.LessonMedia.Add(new LessonMedia
        {
            LessonId = lesson.Id,
            MediaType = model.MediaType,
            RelativePath = savedMedia.RelativePath,
            AltText = model.AltText.Trim(),
            StorageProvider = savedMedia.StorageProvider
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, "Le média a été enregistré.");
    }

    private async Task SyncUserSchoolsAsync(Guid userId, IReadOnlyCollection<Guid> schoolIds, bool canManageStudents, CancellationToken cancellationToken, IReadOnlyCollection<Guid>? managedSchoolIds = null)
    {
        var existingLinks = await dbContext.UserSchools.Where(x => x.AppUserId == userId).ToListAsync(cancellationToken);

        var linksToRemove = existingLinks.Where(link => (managedSchoolIds is null || managedSchoolIds.Contains(link.SchoolId)) && !schoolIds.Contains(link.SchoolId)).ToList();
        if (linksToRemove.Count > 0)
        {
            dbContext.UserSchools.RemoveRange(linksToRemove);
        }

        foreach (var schoolId in schoolIds)
        {
            if (managedSchoolIds is not null && !managedSchoolIds.Contains(schoolId)) continue;

            var link = existingLinks.FirstOrDefault(x => x.SchoolId == schoolId);
            if (link is null)
            {
                dbContext.UserSchools.Add(new UserSchool
                {
                    AppUserId = userId,
                    SchoolId = schoolId,
                    CanManageStudents = canManageStudents
                });
            }
            else
            {
                link.CanManageStudents = canManageStudents;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Löscht eine noch nicht bestätigte Selbstregistrierung endgültig. Der aufrufende
    /// Schulleiter darf ausschließlich Schüler seiner eigenen Schule entfernen.
    /// </summary>
    public async Task<(bool Succeeded, string Message)> DeletePendingSelfRegisteredStudentAsync(
        Guid directorId,
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        var schoolIds = await GetDirectorSchoolIdsAsync(directorId, cancellationToken);
        var profile = await dbContext.StudentProfiles
            .FirstOrDefaultAsync(x => x.AppUserId == studentId && schoolIds.Contains(x.SchoolId), cancellationToken);
        var user = await userManager.FindByIdAsync(studentId.ToString());
        if (profile is null || user is null)
        {
            return (false, "L'élève sélectionné est introuvable.");
        }
        if (!user.IsSelfRegistered || !user.IsBlocked)
        {
            return (false, "Seules les auto-inscriptions encore en attente peuvent être supprimées.");
        }

        dbContext.StudentProfiles.Remove(profile);
        dbContext.UserSchools.RemoveRange(await dbContext.UserSchools
            .Where(x => x.AppUserId == studentId)
            .ToListAsync(cancellationToken));
        dbContext.LevelEnrollments.RemoveRange(await dbContext.LevelEnrollments
            .Where(x => x.StudentId == studentId)
            .ToListAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);

        var deleteResult = await userManager.DeleteAsync(user);
        return deleteResult.Succeeded
            ? (true, "L'auto-inscription en attente a été supprimée définitivement.")
            : (false, string.Join(" ", deleteResult.Errors.Select(x => x.Description)));
    }

    private async Task EnsureStudentProfileStateAsync(
        AppUser user,
        string roleName,
        IReadOnlyCollection<Guid> schoolIds,
        Guid? currentKlasseId,
        string? elternTelefonNummer,
        string? elternEmail,
        CancellationToken cancellationToken)
    {
        var existingProfiles = await dbContext.StudentProfiles.Where(x => x.AppUserId == user.Id).ToListAsync(cancellationToken);

        if (roleName != "Schüler")
        {
            if (existingProfiles.Count > 0)
            {
                dbContext.StudentProfiles.RemoveRange(existingProfiles);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        var firstSchoolId = schoolIds.FirstOrDefault();
        if (firstSchoolId == Guid.Empty)
        {
            return;
        }

        var selectedKlasse = currentKlasseId is Guid klasseId
            ? await dbContext.Klassen.FirstOrDefaultAsync(klasse => klasse.Id == klasseId && klasse.SchoolId == firstSchoolId, cancellationToken)
            : null;
        var profile = existingProfiles.FirstOrDefault(x => x.SchoolId == firstSchoolId);
        if (profile is null)
        {
            profile = new StudentProfile
            {
                AppUserId = user.Id,
                SchoolId = firstSchoolId,
                CompletedLessonsCount = 0
            };
            dbContext.StudentProfiles.Add(profile);
        }

        if (selectedKlasse is not null)
        {
            profile.CurrentKlasseId = selectedKlasse.Id;
            profile.CurrentLevelCode = selectedKlasse.Code;

            var activeCycleId = await dbContext.KursNiveauDurchlaeufe
                .AsNoTracking()
                .Where(durchlauf => durchlauf.SchulID == firstSchoolId
                    && durchlauf.KlasseId == selectedKlasse.Id
                    && durchlauf.EndDatum == null)
                .Select(durchlauf => (Guid?)durchlauf.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var autoAssignAlsBezahlt = selectedKlasse.AutoAssignSkoulouAppAboOnCycleStart && activeCycleId.HasValue;
            if (!autoAssignAlsBezahlt)
            {
                var hasEnrollment = await dbContext.LevelEnrollments.AnyAsync(
                    enrollment => enrollment.StudentId == user.Id && enrollment.KlasseId == selectedKlasse.Id && enrollment.IstAktuell,
                    cancellationToken);
                if (!hasEnrollment)
                {
                    await dbContext.LevelEnrollments.AddAsync(new LevelEnrollment
                    {
                        StudentId = user.Id,
                        KlasseId = selectedKlasse.Id,
                        DurchlaufId = activeCycleId
                    }, cancellationToken);
                }
            }
        }

        profile.ElternTelefonNummer = NormalizeOptional(elternTelefonNummer);
        profile.ElternEmail = NormalizeOptional(elternEmail);

        var obsoleteProfiles = existingProfiles.Where(x => x.SchoolId != firstSchoolId).ToList();
        if (obsoleteProfiles.Count > 0)
        {
            dbContext.StudentProfiles.RemoveRange(obsoleteProfiles);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (selectedKlasse is not null)
        {
            var hasActiveCycle = await dbContext.KursNiveauDurchlaeufe
                .AsNoTracking()
                .AnyAsync(durchlauf => durchlauf.SchulID == firstSchoolId
                    && durchlauf.KlasseId == selectedKlasse.Id
                    && durchlauf.EndDatum == null, cancellationToken);
            var autoAssignAlsBezahlt = selectedKlasse.AutoAssignSkoulouAppAboOnCycleStart && hasActiveCycle;
            if (autoAssignAlsBezahlt)
            {
                var aboService = new AboService(dbContext);
                await aboService.AboZuweisenAsync(user.Id, selectedKlasse.Id, AboTyp.SkoulouApp, cancellationToken: cancellationToken);
            }
        }
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task SyncTeacherAssignmentsAsync(Guid schoolId, IReadOnlyCollection<Guid> teacherIds, CancellationToken cancellationToken)
    {
        var teacherRoleUserIds = (await userManager.GetUsersInRoleAsync(Rollen.Lehrer)).Select(user => user.Id).ToHashSet();
        var existingLinks = await dbContext.UserSchools
            .Where(link => link.SchoolId == schoolId && link.CanManageStudents)
            .ToListAsync(cancellationToken);

        var existingTeacherLinks = existingLinks
            .Where(link => teacherRoleUserIds.Contains(link.AppUserId))
            .ToList();

        var existingTeacherIds = existingTeacherLinks.Select(link => link.AppUserId).ToHashSet();

        var linksToRemove = existingTeacherLinks.Where(link => !teacherIds.Contains(link.AppUserId)).ToList();
        if (linksToRemove.Count > 0)
        {
            dbContext.UserSchools.RemoveRange(linksToRemove);
        }

        var teacherIdsToAdd = teacherIds.Where(id => !existingTeacherIds.Contains(id)).ToList();
        foreach (var teacherId in teacherIdsToAdd)
        {
            dbContext.UserSchools.Add(new UserSchool
            {
                AppUserId = teacherId,
                SchoolId = schoolId,
                CanManageStudents = true
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TeacherSchoolOverview>> GetTeacherSchoolsAsync(Guid teacherId, CancellationToken cancellationToken = default)
    {
        var zuordnungen = await GetAktiveLehrerZuordnungenAsync(teacherId, cancellationToken);
        var eingeschraenkteSchulIds = zuordnungen.Select(z => z.SchulID).Distinct().ToList();

        if (eingeschraenkteSchulIds.Count == 0)
        {
            return [];
        }

        var lehrerIds = (await userManager.GetUsersInRoleAsync("Lehrer")).Select(x => x.Id).ToHashSet();

        var query = dbContext.UserSchools
            .AsNoTracking()
            .Where(link => link.AppUserId == teacherId && link.CanManageStudents);

        query = query.Where(link => eingeschraenkteSchulIds.Contains(link.SchoolId));

        return await query
            .Select(link => new TeacherSchoolOverview
            {
                SchoolId = link.SchoolId,
                SchoolName = link.School.Name,
                City = link.School.City,
                StudentCount = link.School.Students.Count,
                PendingApprovalCount = link.School.Students.Count(student => student.AppUser.IsSelfRegistered && student.AppUser.IsBlocked),
                BlockedStudentCount = link.School.Students.Count(student => student.AppUser.IsBlocked),
                TeacherCount = link.School.UserSchools.Count(l => l.CanManageStudents && lehrerIds.Contains(l.AppUserId))
            })
            .OrderBy(x => x.SchoolName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TeacherSchoolOverview>> GetSchoolLeaderSchoolsAsync(Guid directorId, CancellationToken cancellationToken = default)
    {
        var schoolIds = await GetDirectorSchoolIdsAsync(directorId, cancellationToken);
        if (schoolIds.Count == 0)
        {
            return [];
        }

        var teacherIds = (await userManager.GetUsersInRoleAsync(Rollen.Lehrer)).Select(user => user.Id).ToHashSet();
        var globalSubscriptionPrice = await GetGlobalSubscriptionPriceAsync(cancellationToken);
        var commissionSettings = await dbContext.CommissionSettings
            .AsNoTracking()
            .OrderBy(x => x.ErstelltAmUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var globalSchoolLeaderPercent = commissionSettings?.SchoolLeaderPercent ?? SkoulouAppKonstanten.StandardProvisionSchulleiterProzent;
        var globalTeacherPercent = commissionSettings?.TeacherPercent ?? SkoulouAppKonstanten.StandardProvisionLehrerProzent;

        return await dbContext.Schools
            .AsNoTracking()
            .Where(school => schoolIds.Contains(school.Id))
            .Select(school => new TeacherSchoolOverview
            {
                SchoolId = school.Id,
                SchoolName = school.Name,
                City = school.City,
                StudentCount = school.Students.Count(student => !student.IsArchived),
                PendingApprovalCount = school.Students.Count(student => !student.IsArchived && student.AppUser.IsSelfRegistered && student.AppUser.IsBlocked),
                BlockedStudentCount = school.Students.Count(student => !student.IsArchived && student.AppUser.IsBlocked),
                TeacherCount = school.UserSchools.Count(link => link.CanManageStudents && teacherIds.Contains(link.AppUserId)),
                EffectiveSubscriptionPrice = school.SubscriptionPriceOverride ?? globalSubscriptionPrice,
                SchoolLeaderCommissionPercent = school.SchoolLeaderCommissionPercentOverride ?? globalSchoolLeaderPercent,
                TeacherCommissionPercent = school.TeacherCommissionPercentOverride ?? globalTeacherPercent
            })
            .OrderBy(school => school.SchoolName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StudentApprovalItem>> GetSchoolLeaderStudentsAsync(Guid directorId, CancellationToken cancellationToken = default)
    {
        var schoolIds = await GetDirectorSchoolIdsAsync(directorId, cancellationToken);
        if (schoolIds.Count == 0)
        {
            return [];
        }

        var profiles = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(profile => schoolIds.Contains(profile.SchoolId) && !profile.IsArchived)
            .Select(profile => new StudentApprovalItem
            {
                UserId = profile.AppUserId,
                FullName = profile.AppUser.FullName,
                Email = profile.AppUser.Email ?? string.Empty,
                SchoolName = profile.School.Name,
                SchoolId = profile.SchoolId,
                CurrentLevelCode = profile.CurrentLevelCode,
                CurrentKlasseId = profile.CurrentKlasseId,
                CurrentKlasseName = profile.CurrentKlasse != null
                    ? profile.CurrentKlasse.Title
                    : profile.CurrentLevelCode,
                IsBlocked = profile.AppUser.IsBlocked,
                IsSelfRegistered = profile.AppUser.IsSelfRegistered,
                CompletedLessonsCount = profile.CompletedLessonsCount,
                EnrolledAtUtc = profile.AppUser.CreatedAtUtc,
                HasPaidEnrollment = profile.AppUser.LevelEnrollments.Any(enrollment => enrollment.IstAktuell && enrollment.IsPaid),
                HasActiveSubscription = profile.AppUser.LevelEnrollments.Any(enrollment => enrollment.IstAktuell && enrollment.IsUnlocked)
            })
            .OrderBy(profile => profile.SchoolName)
            .ThenBy(profile => profile.FullName)
            .ToListAsync(cancellationToken);

        await ApplyStudentProgressScoresAsync(profiles, cancellationToken);
        await ApplySchoolFeeInfoAsync(profiles, cancellationToken);
        return profiles;
    }

    public async Task<(bool Succeeded, string Message)> AssignStudentsToKlasseAsync(
        Guid directorId,
        Guid klasseId,
        IReadOnlyCollection<Guid> studentIds,
        CancellationToken cancellationToken = default)
    {
        if (studentIds.Count == 0)
        {
            return (false, "Sélectionne au moins un élève.");
        }

        var schoolIds = await GetDirectorSchoolIdsAsync(directorId, cancellationToken);
        if (schoolIds.Count == 0)
        {
            return (false, "Aucune école n'est associée à votre compte.");
        }

        var klasse = await dbContext.Klassen
            .FirstOrDefaultAsync(x => x.Id == klasseId && x.SchoolId != null && schoolIds.Contains(x.SchoolId.Value), cancellationToken);
        if (klasse is null)
        {
            return (false, "Classe introuvable ou non autorisée.");
        }

        var targetStudentIds = studentIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var profiles = await dbContext.StudentProfiles
            .Where(profile => targetStudentIds.Contains(profile.AppUserId)
                              && profile.SchoolId == klasse.SchoolId
                              && profile.CurrentKlasseId == null)
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0)
        {
            return (false, "Aucun élève sans classe active ne correspond à la sélection.");
        }

        foreach (var profile in profiles)
        {
            profile.CurrentKlasseId = klasse.Id;
            profile.CurrentLevelCode = klasse.Code;

            var hasEnrollment = await dbContext.LevelEnrollments.AnyAsync(
                enrollment => enrollment.StudentId == profile.AppUserId && enrollment.KlasseId == klasse.Id && enrollment.IstAktuell,
                cancellationToken);
            if (!hasEnrollment)
            {
                var activeCycleId = await dbContext.KursNiveauDurchlaeufe.AsNoTracking()
                    .Where(cycle => cycle.KlasseId == klasse.Id && cycle.SchulID == klasse.SchoolId && cycle.EndDatum == null)
                    .Select(cycle => (Guid?)cycle.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                await dbContext.LevelEnrollments.AddAsync(new LevelEnrollment
                {
                    StudentId = profile.AppUserId,
                    KlasseId = klasse.Id,
                    DurchlaufId = activeCycleId,
                    PriceAtEnrollment = klasse.Price
                }, cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var hasActiveCycle = await dbContext.KursNiveauDurchlaeufe
            .AsNoTracking()
            .AnyAsync(durchlauf => durchlauf.SchulID == klasse.SchoolId
                && durchlauf.KlasseId == klasse.Id
                && durchlauf.EndDatum == null, cancellationToken);

        if (klasse.AutoAssignSkoulouAppAboOnCycleStart && hasActiveCycle)
        {
            var aboService = new AboService(dbContext);
            foreach (var profile in profiles)
            {
                await aboService.AboZuweisenAsync(profile.AppUserId, klasse.Id, AboTyp.SkoulouApp, cancellationToken: cancellationToken);
            }
        }

        return (true, $"{profiles.Count} élève(s) attribué(s) à {klasse.Title}.");
    }

    public async Task<IReadOnlyList<StudentApprovalItem>> GetTeacherStudentsAsync(Guid teacherId, CancellationToken cancellationToken = default)
    {
        var teacherSchoolIds = await dbContext.UserSchools
            .AsNoTracking()
            .Where(link => link.AppUserId == teacherId && link.CanManageStudents)
            .Select(link => link.SchoolId)
            .ToListAsync(cancellationToken);

        var zuordnungen = await GetAktiveLehrerZuordnungenAsync(teacherId, cancellationToken);
        var zuordnungsSchulIds = zuordnungen.Select(z => z.SchulID).Distinct().ToList();
        var zuordnungsSchluessel = zuordnungen
            .Select(z => z.SchulID + "|" + z.KlasseId)
            .ToHashSet();

        if (zuordnungen.Count == 0)
        {
            return [];
        }

        var profiles = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(profile => teacherSchoolIds.Contains(profile.SchoolId) && !profile.IsArchived)
            .Select(profile => new StudentApprovalItem
            {
                UserId = profile.AppUserId,
                FullName = profile.AppUser.FullName,
                Email = profile.AppUser.Email ?? string.Empty,
                SchoolName = profile.School.Name,
                SchoolId = profile.SchoolId,
                CurrentLevelCode = profile.CurrentLevelCode,
                CurrentKlasseId = profile.CurrentKlasseId,
                CurrentKlasseName = profile.CurrentKlasse != null
                    ? profile.CurrentKlasse.Title
                    : profile.CurrentLevelCode,
                IsBlocked = profile.AppUser.IsBlocked,
                IsSelfRegistered = profile.AppUser.IsSelfRegistered,
                CompletedLessonsCount = profile.CompletedLessonsCount,
                EnrolledAtUtc = profile.AppUser.CreatedAtUtc,
                HasPaidEnrollment = profile.AppUser.LevelEnrollments.Any(e => e.IstAktuell && e.IsPaid),
                HasActiveSubscription = profile.AppUser.LevelEnrollments.Any(e => e.IstAktuell && e.IsUnlocked)
            })
            .OrderBy(x => x.SchoolName)
            .ThenBy(x => x.FullName)
            .ToListAsync(cancellationToken);

        profiles = profiles
            .Where(p => zuordnungsSchulIds.Contains(p.SchoolId)
                && p.CurrentKlasseId.HasValue
                && zuordnungsSchluessel.Contains(p.SchoolId + "|" + p.CurrentKlasseId.Value))
            .ToList();

        await ApplyStudentProgressScoresAsync(profiles, cancellationToken);
        await ApplySchoolFeeInfoAsync(profiles, cancellationToken);

        return profiles;
    }

    private async Task ApplyStudentProgressScoresAsync(IReadOnlyList<StudentApprovalItem> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        var studentIds = items.Select(x => x.UserId).ToList();
        var combinedScores = await GetCombinedProgressScoresAsync(studentIds, cancellationToken);

        foreach (var item in items)
        {
            item.IsSkoulouPlus = item.HasActiveSubscription;

            if (combinedScores.TryGetValue(item.UserId, out var combined) && combined.Count > 0)
            {
                item.CombinedScorePercent = combined.AverageScorePercent;
                item.CombinedScoreCount = combined.Count;
            }
        }
    }

    private async Task ApplySchoolFeeInfoAsync(IReadOnlyList<StudentApprovalItem> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        var feeLookup = await GetCurrentSchoolFeeLookupAsync(items.Select(x => x.UserId).ToList(), cancellationToken);
        foreach (var item in items)
        {
            if (feeLookup.TryGetValue(item.UserId, out var fee))
            {
                item.SchoolFeeAmount = fee.Amount;
                item.SchoolFeePaidAmount = fee.PaidAmount;
                item.HasActiveSchoolFeeCycle = fee.HasCycle;
            }
        }
    }

    private async Task<Dictionary<Guid, SchoolFeeSnapshot>> GetCurrentSchoolFeeLookupAsync(
        IReadOnlyCollection<Guid> studentIds,
        CancellationToken cancellationToken)
    {
        if (studentIds.Count == 0)
        {
            return [];
        }

        var profiles = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(profile => studentIds.Contains(profile.AppUserId))
            .Select(profile => new
            {
                profile.AppUserId,
                profile.SchoolId,
                profile.CurrentKlasseId
            })
            .ToListAsync(cancellationToken);

        var currentClassProfiles = profiles
            .Where(profile => profile.CurrentKlasseId.HasValue)
            .ToList();
        if (currentClassProfiles.Count == 0)
        {
            return [];
        }

        var currentClassKeys = currentClassProfiles
            .Select(profile => profile.SchoolId + "|" + profile.CurrentKlasseId!.Value)
            .ToHashSet();

        var durchlaeufe = await dbContext.KursNiveauDurchlaeufe
            .AsNoTracking()
            .Where(durchlauf => currentClassKeys.Contains(durchlauf.SchulID + "|" + durchlauf.KlasseId))
            .Select(durchlauf => new
            {
                durchlauf.Id,
                durchlauf.SchulID,
                durchlauf.KlasseId,
                durchlauf.NiveauPreis,
                durchlauf.StartDatum,
                durchlauf.EndDatum
            })
            .ToListAsync(cancellationToken);

        var candidates = (
            from profile in currentClassProfiles
            join durchlauf in durchlaeufe
                on new { profile.SchoolId, KlasseId = profile.CurrentKlasseId!.Value } equals new { SchoolId = durchlauf.SchulID, durchlauf.KlasseId } into matchingDurchlaeufe
            from durchlauf in matchingDurchlaeufe.DefaultIfEmpty()
            select new
            {
                StudentId = profile.AppUserId,
                DurchlaufId = durchlauf?.Id,
                NiveauPreis = durchlauf?.NiveauPreis,
                StartDatum = durchlauf?.StartDatum,
                EndDatum = durchlauf?.EndDatum,
                HasCycle = durchlauf is not null
            })
            .ToList();

        var durchlaufIds = candidates
            .Where(x => x.DurchlaufId.HasValue)
            .Select(x => x.DurchlaufId!.Value)
            .Distinct()
            .ToList();
        var payments = await dbContext.RatenZahlungen
            .AsNoTracking()
            .Where(x => durchlaufIds.Contains(x.DurchlaufID) && studentIds.Contains(x.StudentId))
            .GroupBy(x => new { x.DurchlaufID, x.StudentId })
            .Select(group => new
            {
                group.Key.DurchlaufID,
                group.Key.StudentId,
                PaidAmount = group.Sum(x => x.Betrag)
            })
            .ToListAsync(cancellationToken);
        var paymentLookup = payments.ToDictionary(x => (x.DurchlaufID, x.StudentId), x => x.PaidAmount);

        return candidates
            .GroupBy(x => x.StudentId)
            .Select(group => group
                .OrderByDescending(x => x.HasCycle)
                .ThenByDescending(x => x.EndDatum is null)
                .ThenByDescending(x => x.StartDatum)
                .First())
            .ToDictionary(
                x => x.StudentId,
                x => new SchoolFeeSnapshot(
                    x.NiveauPreis,
                    x.DurchlaufId.HasValue ? paymentLookup.GetValueOrDefault((x.DurchlaufId.Value, x.StudentId)) : 0m,
                    x.HasCycle));
    }

    private sealed record SchoolFeeSnapshot(decimal? Amount, decimal PaidAmount, bool HasCycle);

    public async Task<IReadOnlyList<StudentProgressItem>> GetTeacherStudentProgressAsync(Guid teacherId, CancellationToken cancellationToken = default)
    {
        var teacherSchoolIds = await dbContext.UserSchools
            .AsNoTracking()
            .Where(link => link.AppUserId == teacherId && link.CanManageStudents)
            .Select(link => link.SchoolId)
            .ToListAsync(cancellationToken);

        var students = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(profile => teacherSchoolIds.Contains(profile.SchoolId) && !profile.IsArchived)
            .Select(profile => new
            {
                profile.AppUserId,
                profile.AppUser.FullName,
                SchoolName = profile.School.Name,
                profile.SchoolId,
                profile.CurrentLevelCode,
                profile.CurrentKlasseId,
                profile.AppUser.IsBlocked,
                profile.CompletedLessonsCount
            })
            .ToListAsync(cancellationToken);

        var progressZuordnungen = await GetAktiveLehrerZuordnungenAsync(teacherId, cancellationToken);
        if (progressZuordnungen.Count == 0)
        {
            return [];
        }

        var progressSchulIds = progressZuordnungen.Select(z => z.SchulID).Distinct().ToHashSet();
        var progressSchluessel = progressZuordnungen
            .Select(z => z.SchulID + "|" + z.KlasseId)
            .ToHashSet();

        students = students
            .Where(s => progressSchulIds.Contains(s.SchoolId)
                && s.CurrentKlasseId.HasValue
                && progressSchluessel.Contains(s.SchoolId + "|" + s.CurrentKlasseId.Value))
            .ToList();

        var studentIds = students.Select(x => x.AppUserId).ToList();

        var solvedExercises = await dbContext.ExerciseAttempts
            .AsNoTracking()
            .Where(x => x.IsCorrect && studentIds.Contains(x.StudentId))
            .GroupBy(x => x.StudentId)
            .Select(g => new { StudentId = g.Key, Count = g.Select(a => a.ExerciseId).Distinct().Count() })
            .ToListAsync(cancellationToken);

        var examStats = await dbContext.ExamAttempts
            .AsNoTracking()
            .Where(x => x.IsCompleted && studentIds.Contains(x.StudentId))
            .GroupBy(x => x.StudentId)
            .Select(g => new
            {
                StudentId = g.Key,
                Taken = g.Count(),
                Passed = g.Count(a => a.IsPassed),
                BestScore = g.Max(a => a.ScorePercent),
                LastActivity = g.Max(a => a.SubmittedAtUtc)
            })
            .ToListAsync(cancellationToken);

        var lessonActivity = await dbContext.LessonProgresses
            .AsNoTracking()
            .Where(x => studentIds.Contains(x.StudentId))
            .GroupBy(x => x.StudentId)
            .Select(g => new { StudentId = g.Key, LastActivity = g.Max(a => a.LastActivityUtc) })
            .ToListAsync(cancellationToken);

        var combinedScores = await GetCombinedProgressScoresAsync(studentIds, cancellationToken);
        var paidStudentIds = await dbContext.LevelEnrollments
            .AsNoTracking()
            .Where(enrollment => studentIds.Contains(enrollment.StudentId) && enrollment.IstAktuell && enrollment.IsPaid)
            .Select(enrollment => enrollment.StudentId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        return students
            .Select(student =>
            {
                var exam = examStats.FirstOrDefault(x => x.StudentId == student.AppUserId);
                var examLast = exam?.LastActivity;
                var lessonLast = lessonActivity.FirstOrDefault(x => x.StudentId == student.AppUserId)?.LastActivity;
                DateTime? lastActivity = examLast is null
                    ? lessonLast
                    : lessonLast is null ? examLast : (examLast > lessonLast ? examLast : lessonLast);

                var isSkoulouPlus = paidStudentIds.Contains(student.AppUserId);
                combinedScores.TryGetValue(student.AppUserId, out var combined);

                return new StudentProgressItem
                {
                    UserId = student.AppUserId,
                    FullName = student.FullName,
                    SchoolId = student.SchoolId,
                    SchoolName = student.SchoolName,
                    CurrentLevelCode = student.CurrentLevelCode,
                    IsBlocked = student.IsBlocked,
                    CompletedLessonsCount = student.CompletedLessonsCount,
                    SolvedExerciseCount = solvedExercises.FirstOrDefault(x => x.StudentId == student.AppUserId)?.Count ?? 0,
                    ExamsTaken = exam?.Taken ?? 0,
                    ExamsPassed = exam?.Passed ?? 0,
                    BestExamScorePercent = exam?.BestScore ?? 0,
                    IsSkoulouPlus = isSkoulouPlus,
                    CombinedScorePercent = combined.Count > 0 ? combined.AverageScorePercent : null,
                    CombinedScoreCount = combined.Count,
                    LastActivityUtc = lastActivity
                };
            })
            .OrderBy(x => x.SchoolName)
            .ThenBy(x => x.FullName)
            .ToList();
    }

    public async Task<(bool Succeeded, string Message)> ArchiveStudentsAsync(
        Guid directorId,
        IReadOnlyCollection<Guid> studentIds,
        CancellationToken cancellationToken = default)
    {
        if (studentIds is null || studentIds.Count == 0)
        {
            return (false, "Sélectionne au moins un élève à archiver.");
        }

        var schoolIds = await GetDirectorSchoolIdsAsync(directorId, cancellationToken);
        if (schoolIds.Count == 0)
        {
            return (false, "Aucune école n'est associée à votre compte.");
        }

        var profiles = await dbContext.StudentProfiles
            .Where(profile => studentIds.Contains(profile.AppUserId)
                              && schoolIds.Contains(profile.SchoolId)
                              && !profile.IsArchived)
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0)
        {
            return (false, "Aucun élève actif sélectionné n'est archivable.");
        }

        foreach (var profile in profiles)
        {
            profile.IsArchived = true;
            profile.GeaendertAmUtc = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, profiles.Count == 1
            ? "L'élève sélectionné a été archivé."
            : $"{profiles.Count} élèves ont été archivés.");
    }

    public async Task<(bool Succeeded, string Message)> SetSchoolUserBlockedStateAsync(
        Guid directorId, Guid userId, bool isBlocked, CancellationToken cancellationToken = default)
    {
        var allowedRoles = await GetManageableSchoolUserRolesAsync(directorId, cancellationToken);
        var schoolIds = await GetDirectorSchoolIdsAsync(directorId, cancellationToken);
        var user = await dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId
                && candidate.UserSchools.Any(link => schoolIds.Contains(link.SchoolId)), cancellationToken);
        if (user is null || !(await userManager.GetRolesAsync(user)).Any(allowedRoles.Contains))
        {
            return (false, "Vous n'êtes pas autorisé à modifier le statut de cet utilisateur.");
        }

        var changed = await SetStudentBlockedStateAsync(userId, isBlocked, cancellationToken: cancellationToken);
        if (!changed)
        {
            return (false, "Un élève ayant un abonnement payé ne peut pas être bloqué ici.");
        }

        return (true, isBlocked ? "L'utilisateur a été bloqué." : "L'utilisateur a été activé.");
    }

    public async Task<bool> SetStudentBlockedStateAsync(Guid studentId, bool isBlocked, bool allowBlockingPaidStudent = false, CancellationToken cancellationToken = default)
    {
        var student = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == studentId, cancellationToken);
        if (student is null)
        {
            return false;
        }

        // Ein Schüler mit mindestens einem bezahlten Niveau darf nur vom SuperAdmin
        // (allowBlockingPaidStudent = true) blockiert werden. Der Schulleiter kann dies nicht.
        if (isBlocked && !allowBlockingPaidStudent)
        {
            var hatBezahltesNiveau = await dbContext.LevelEnrollments
                .AsNoTracking()
                .AnyAsync(e => e.StudentId == studentId && e.IsPaid, cancellationToken);
            if (hatBezahltesNiveau)
            {
                return false;
            }
        }

        student.IsBlocked = isBlocked;
        student.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<List<Guid>> GetDirectorSchoolIdsAsync(Guid directorId, CancellationToken cancellationToken)
    {
        var director = await userManager.FindByIdAsync(directorId.ToString());
        if (director is null || !await userManager.IsInRoleAsync(director, Rollen.SchulLeiter)
            && !await userManager.IsInRoleAsync(director, Rollen.Koordinator)) return [];
        return await dbContext.UserSchools
            .AsNoTracking()
            .Where(link => link.AppUserId == directorId && link.CanManageStudents)
            .Select(link => link.SchoolId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Liefert die aktiven Klassen-Zuordnungen eines Lehrers.
    /// Eine leere Liste bedeutet, dass der Lehrer keine Schüler sehen darf.
    /// </summary>
    private async Task<List<LehrerNiveauZuordnung>> GetAktiveLehrerZuordnungenAsync(Guid teacherId, CancellationToken cancellationToken)
        => await dbContext.LehrerNiveauZuordnungen
            .AsNoTracking()
            .Where(z => z.LehrerID == teacherId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SchoolUserItem>> GetSchoolUsersAsync(Guid directorId, CancellationToken cancellationToken = default)
    {
        var schoolIds = await GetDirectorSchoolIdsAsync(directorId, cancellationToken);
        if (schoolIds.Count == 0)
        {
            return [];
        }

        var allowedRoles = await GetManageableSchoolUserRolesAsync(directorId, cancellationToken);
        if (allowedRoles.Count == 0)
        {
            return [];
        }

        var users = await dbContext.Users
            .AsNoTracking()
            .Include(x => x.UserSchools)
            .ThenInclude(link => link.School)
            .Where(x => x.Id != directorId && x.UserSchools.Any(link => schoolIds.Contains(link.SchoolId)))
            .ToListAsync(cancellationToken);

        var result = new List<SchoolUserItem>();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            var roleName = roles.FirstOrDefault() ?? Rollen.Schueler;

            // Nur Rollen anzeigen, die der aktuelle Manager verwalten darf.
            if (!allowedRoles.Contains(roleName))
            {
                continue;
            }

            result.Add(new SchoolUserItem
            {
                UserId = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                RoleName = roleName,
                IsBlocked = user.IsBlocked,
                IsSelfRegistered = user.IsSelfRegistered,
                Schools = user.UserSchools
                    .Where(link => schoolIds.Contains(link.SchoolId))
                    .Select(link => link.School.Name)
                    .OrderBy(name => name)
                    .ToList()
            });
        }

        return result
            .OrderBy(x => x.RoleName == Rollen.Lehrer ? 0 : 1)
            .ThenBy(x => x.FullName)
            .ToList();
    }

    public async Task<UserFormModel?> GetSchoolUserForEditAsync(Guid directorId, Guid userId, CancellationToken cancellationToken = default)
    {
        var schoolIds = await GetDirectorSchoolIdsAsync(directorId, cancellationToken);
        if (schoolIds.Count == 0)
        {
            return null;
        }

        var allowedRoles = await GetManageableSchoolUserRolesAsync(directorId, cancellationToken);
        if (allowedRoles.Count == 0)
        {
            return null;
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .Include(x => x.UserSchools)
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null || !user.UserSchools.Any(link => schoolIds.Contains(link.SchoolId)))
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        var roleName = roles.FirstOrDefault() ?? Rollen.Schueler;
        if (!allowedRoles.Contains(roleName))
        {
            return null;
        }

        var teacherClassAssignments = roleName == Rollen.Lehrer
            ? await dbContext.LehrerNiveauZuordnungen
                .AsNoTracking()
                .Where(assignment => assignment.LehrerID == user.Id && schoolIds.Contains(assignment.SchulID))
                .Select(assignment => new TeacherClassAssignmentFormModel
                {
                    SchoolId = assignment.SchulID,
                    KlasseId = assignment.KlasseId,
                    Wochenstunden = assignment.Wochenstunden
                })
                .ToListAsync(cancellationToken)
            : [];

        var teacherFachIds = roleName == Rollen.Lehrer
            ? await dbContext.LehrerFachZuordnungen
                .AsNoTracking()
                .Where(assignment => assignment.LehrerId == user.Id && schoolIds.Contains(assignment.Fach.SchoolId))
                .Select(assignment => assignment.FachId)
                .Distinct()
                .ToListAsync(cancellationToken)
            : [];

        return new UserFormModel
        {
            UserId = user.Id,
            Vorname = user.Vorname,
            Nachname = user.Nachname,
            Email = user.Email ?? string.Empty,
            TelefonNummer = user.TelefonNummer,
            RoleName = roleName,
            IsBlocked = user.IsBlocked,
            IsSelfRegistered = user.IsSelfRegistered,
            SchoolIds = user.UserSchools
                .Where(link => schoolIds.Contains(link.SchoolId))
                .Select(link => link.SchoolId)
                .ToList(),
            CurrentKlasseId = await dbContext.StudentProfiles
                .Where(profile => profile.AppUserId == user.Id && schoolIds.Contains(profile.SchoolId))
                .Select(profile => profile.CurrentKlasseId)
                .FirstOrDefaultAsync(cancellationToken),
            FachIds = teacherFachIds,
            TeacherClassAssignments = teacherClassAssignments,
            CoordinatorFinanceAccesses = roleName == Rollen.Koordinator
                ? user.UserSchools
                    .Where(link => schoolIds.Contains(link.SchoolId))
                    .Select(link => new CoordinatorFinanceAccessFormModel
                    {
                        SchoolId = link.SchoolId,
                        Menu = link.CanAccessFinances,
                        Dashboard = link.CanViewFinanceDashboard,
                        Entries = link.CanCreateFinanceEntries,
                        Cycles = link.CanManageCyclePayments,
                        Billing = link.CanAccessBilling
                    })
                    .ToList()
                : []
        };
    }

    public async Task<(bool Succeeded, string Message)> SaveSchoolUserAsync(Guid directorId, UserFormModel model, CancellationToken cancellationToken = default)
    {
        var allowedRoles = await GetManageableSchoolUserRolesAsync(directorId, cancellationToken);
        if (allowedRoles.Count == 0)
        {
            return (false, "Vous n'êtes pas autorisé à gérer des utilisateurs scolaires.");
        }

        if (!allowedRoles.Contains(model.RoleName))
        {
            return (false, "Vous n'êtes pas autorisé à créer ou modifier ce rôle.");
        }

        var directorSchoolIds = await GetDirectorSchoolIdsAsync(directorId, cancellationToken);
        if (directorSchoolIds.Count == 0)
        {
            return (false, "Aucune école n'est associée à votre compte.");
        }

        var selectedSchoolIds = model.SchoolIds
            .Where(schoolId => schoolId != Guid.Empty)
            .Distinct()
            .ToList();

        if (model.RoleName == Rollen.Schueler && selectedSchoolIds.Count > 1)
        {
            return (false, "Un élève ne peut être attribué qu'à une seule école.");
        }

        if (selectedSchoolIds.Count == 0 || selectedSchoolIds.Any(schoolId => !directorSchoolIds.Contains(schoolId)))
        {
            return (false, "Veuillez choisir une ou plusieurs de vos écoles.");
        }

        var financeAccesses = model.CoordinatorFinanceAccesses;
        if (financeAccesses.Count > 0)
        {
            var manager = await userManager.FindByIdAsync(directorId.ToString());
            if (model.RoleName != Rollen.Koordinator || manager is null
                || !await userManager.IsInRoleAsync(manager, Rollen.SchulLeiter)
                || financeAccesses.Any(access => !selectedSchoolIds.Contains(access.SchoolId))
                || financeAccesses.Select(access => access.SchoolId).Distinct().Count() != selectedSchoolIds.Count
                || financeAccesses.Select(access => (access.Menu, access.Dashboard, access.Entries, access.Cycles, access.Billing))
                    .Distinct().Count() != 1)
            {
                return (false, "Les accès financiers ne peuvent être définis que pour un coordinateur de vos écoles.");
            }
        }

        if (model.RoleName == Rollen.Schueler && model.CurrentKlasseId is Guid klasseId)
        {
            var klasseBelongsToSchool = await dbContext.Klassen.AnyAsync(
                klasse => klasse.Id == klasseId && klasse.SchoolId == selectedSchoolIds[0], cancellationToken);
            if (!klasseBelongsToSchool)
            {
                return (false, "Veuillez choisir une classe de l'école sélectionnée.");
            }
        }

        model.SchoolIds = selectedSchoolIds;

        // Beim Bearbeiten sicherstellen, dass der Nutzer wirklich zur Schule des Directeurs gehört.
        if (model.UserId.HasValue)
        {
            var existing = await GetSchoolUserForEditAsync(directorId, model.UserId.Value, cancellationToken);
            if (existing is null)
            {
                return (false, "Vous n'êtes pas autorisé à modifier cet utilisateur.");
            }

            if (model.RoleName is Rollen.Koordinator or Rollen.Lehrer)
            {
                var otherSchoolIds = await dbContext.UserSchools.AsNoTracking()
                    .Where(link => link.AppUserId == model.UserId.Value && !directorSchoolIds.Contains(link.SchoolId))
                    .Select(link => link.SchoolId)
                    .ToListAsync(cancellationToken);
                model.SchoolIds = selectedSchoolIds.Concat(otherSchoolIds).Distinct().ToList();
            }
        }
        // Schüler eines Directeurs sind sofort akzeptiert (nicht blockiert).
        if (model.RoleName == Rollen.Schueler)
        {
            model.IsBlocked = false;
        }

        var result = await SaveUserAsync(model, cancellationToken, model.RoleName == Rollen.Lehrer ? directorSchoolIds : null);
        if (result.Succeeded && financeAccesses.Count > 0)
        {
            var coordinator = model.UserId.HasValue
                ? await userManager.FindByIdAsync(model.UserId.Value.ToString())
                : await userManager.FindByEmailAsync(model.Email.Trim());
            if (coordinator is null)
            {
                throw new InvalidOperationException("Le coordinateur enregistré est introuvable.");
            }

            var financeSchoolIds = financeAccesses.Select(access => access.SchoolId).ToList();
            var coordinatorLinks = await dbContext.UserSchools
                .Where(link => link.AppUserId == coordinator.Id && financeSchoolIds.Contains(link.SchoolId))
                .ToListAsync(cancellationToken);
            foreach (var financeAccess in financeAccesses)
            {
                var coordinatorLink = coordinatorLinks.Single(link => link.SchoolId == financeAccess.SchoolId);
                var financeMenu = financeAccess.Menu && (financeAccess.Dashboard || financeAccess.Entries || financeAccess.Cycles || financeAccess.Billing);
                coordinatorLink.CanAccessFinances = financeMenu;
                coordinatorLink.CanViewFinanceDashboard = financeMenu && financeAccess.Dashboard;
                coordinatorLink.CanCreateFinanceEntries = financeMenu && financeAccess.Entries;
                coordinatorLink.CanManageCyclePayments = financeMenu && financeAccess.Cycles;
                coordinatorLink.CanAccessBilling = financeMenu && financeAccess.Billing;
            }
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (result.Succeeded && model.RoleName == Rollen.Lehrer)
        {
            var assignments = model.TeacherClassAssignments
                .Where(assignment => selectedSchoolIds.Contains(assignment.SchoolId))
                .Select(assignment => (assignment.SchoolId, assignment.KlasseId, assignment.Wochenstunden))
                .ToList();

            var teacherId = model.UserId;
            if (!teacherId.HasValue)
            {
                teacherId = await dbContext.Users
                    .Where(user => user.Email == model.Email.Trim())
                    .Select(user => (Guid?)user.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (!teacherId.HasValue)
            {
                if (assignments.Count == 0)
                {
                    return result;
                }

                return (false, "L'enseignant créé est introuvable.");
            }

            await SetLehrerNiveauZuordnungenAsync(directorId, teacherId.Value, assignments, cancellationToken);
            await SetLehrerFachZuordnungenAsync(directorId, teacherId.Value, model.FachIds, cancellationToken);

            var obsoleteFachAssignments = await dbContext.LehrerFachZuordnungen
                .Where(assignment => assignment.LehrerId == teacherId.Value
                    && directorSchoolIds.Contains(assignment.Fach.SchoolId)
                    && !selectedSchoolIds.Contains(assignment.Fach.SchoolId))
                .ToListAsync(cancellationToken);
            var obsoleteAssignments = await dbContext.LehrerNiveauZuordnungen
                .Where(assignment => assignment.LehrerID == teacherId.Value && directorSchoolIds.Contains(assignment.SchulID) && !selectedSchoolIds.Contains(assignment.SchulID))
                .ToListAsync(cancellationToken);

            if (obsoleteAssignments.Count > 0 || obsoleteFachAssignments.Count > 0)
            {
                dbContext.LehrerNiveauZuordnungen.RemoveRange(obsoleteAssignments);
                dbContext.LehrerFachZuordnungen.RemoveRange(obsoleteFachAssignments);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<FachOptionItem>> GetSchoolFachOptionsForDirectionAsync(Guid directorId, Guid? schoolId = null, CancellationToken cancellationToken = default)
    {
        var directorSchoolIds = await GetDirectorSchoolIdsAsync(directorId, cancellationToken);
        if (directorSchoolIds.Count == 0)
        {
            return [];
        }

        var targetSchoolIds = schoolId.HasValue
            ? (directorSchoolIds.Contains(schoolId.Value) ? [schoolId.Value] : [])
            : directorSchoolIds;

        if (targetSchoolIds.Count == 0)
        {
            return [];
        }

        return await dbContext.Faecher
            .AsNoTracking()
            .Where(fach => targetSchoolIds.Contains(fach.SchoolId))
            .Select(fach => new FachOptionItem
            {
                FachId = fach.Id,
                Name = fach.Name,
                SchoolId = fach.SchoolId,
                SchoolName = fach.School.Name,
                IstLehrerZugewiesen = dbContext.LehrerFachZuordnungen.Any(zuordnung => zuordnung.FachId == fach.Id)
            })
            .OrderBy(item => item.SchoolName)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<(bool Succeeded, string Message)> DeleteSchoolFachForDirectionAsync(Guid directorId, Guid fachId, CancellationToken cancellationToken = default)
    {
        var fach = await dbContext.Faecher
            .AsNoTracking()
            .Where(item => item.Id == fachId)
            .Select(item => new { item.Id, item.SchoolId, item.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (fach is null)
        {
            return (false, "La matière est introuvable.");
        }

        var canManageSchool = await dbContext.UserSchools
            .AsNoTracking()
            .AnyAsync(link => link.AppUserId == directorId && link.SchoolId == fach.SchoolId && link.CanManageStudents, cancellationToken);
        if (!canManageSchool)
        {
            return (false, "Vous ne pouvez pas supprimer cette matière.");
        }

        var isAssigned = await dbContext.LehrerFachZuordnungen
            .AsNoTracking()
            .AnyAsync(assignment => assignment.FachId == fachId, cancellationToken);
        if (isAssigned)
        {
            return (false, "Cette matière est déjà attribuée à un enseignant et ne peut pas être supprimée.");
        }

        var entity = await dbContext.Faecher.FirstOrDefaultAsync(item => item.Id == fachId, cancellationToken);
        if (entity is null)
        {
            return (false, "La matière est introuvable.");
        }

        dbContext.Faecher.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, "La matière a été supprimée.");
    }

    public async Task<IReadOnlyList<LehrerFachZuordnungItem>> GetLehrerFachZuordnungenAsync(Guid directorId, Guid teacherId, CancellationToken cancellationToken = default)
    {
        var lehrerSchulIds = await GetGemeinsameLehrerSchulenMitDirektionAsync(directorId, teacherId, cancellationToken);
        if (lehrerSchulIds.Count == 0)
        {
            return [];
        }

        var schulen = await dbContext.Schools
            .AsNoTracking()
            .Where(s => lehrerSchulIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(cancellationToken);

        var faecher = await dbContext.Faecher
            .AsNoTracking()
            .Where(fach => lehrerSchulIds.Contains(fach.SchoolId))
            .OrderBy(fach => fach.Name)
            .Select(fach => new { fach.Id, fach.Name, fach.SchoolId })
            .ToListAsync(cancellationToken);

        var aktiveFachIds = (await dbContext.LehrerFachZuordnungen
            .AsNoTracking()
            .Where(zuordnung => zuordnung.LehrerId == teacherId)
            .Select(zuordnung => zuordnung.FachId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        return faecher
            .Join(
                schulen,
                fach => fach.SchoolId,
                schule => schule.Id,
                (fach, schule) => new LehrerFachZuordnungItem
                {
                    SchulID = schule.Id,
                    SchulName = schule.Name,
                    FachId = fach.Id,
                    FachName = fach.Name,
                    IstZugeordnet = aktiveFachIds.Contains(fach.Id)
                })
            .OrderBy(item => item.SchulName)
            .ThenBy(item => item.FachName)
            .ToList();
    }

    public async Task SetLehrerFachZuordnungenAsync(Guid directorId, Guid teacherId, IReadOnlyList<Guid> fachIds, CancellationToken cancellationToken = default)
    {
        var lehrerSchulIds = await GetGemeinsameLehrerSchulenMitDirektionAsync(directorId, teacherId, cancellationToken);
        if (lehrerSchulIds.Count == 0)
        {
            return;
        }

        var bestehende = await dbContext.LehrerFachZuordnungen
            .Where(zuordnung => zuordnung.LehrerId == teacherId && lehrerSchulIds.Contains(zuordnung.Fach.SchoolId))
            .ToListAsync(cancellationToken);

        dbContext.LehrerFachZuordnungen.RemoveRange(bestehende);

        var erlaubteFachIds = await dbContext.Faecher
            .AsNoTracking()
            .Where(fach => lehrerSchulIds.Contains(fach.SchoolId))
            .Select(fach => fach.Id)
            .ToListAsync(cancellationToken);

        var erlaubteFachIdSet = erlaubteFachIds.ToHashSet();
        foreach (var fachId in (fachIds ?? []).Where(id => erlaubteFachIdSet.Contains(id)).Distinct())
        {
            await dbContext.LehrerFachZuordnungen.AddAsync(new LehrerFachZuordnung
            {
                LehrerId = teacherId,
                FachId = fachId
            }, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<(bool Succeeded, string Message, Guid? FachId)> CreateFachForSchoolByDirectionAsync(Guid directorId, Guid schoolId, string fachName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fachName))
        {
            return (false, "Veuillez saisir une matière.", null);
        }

        var directorSchoolIds = await GetDirectorSchoolIdsAsync(directorId, cancellationToken);
        if (!directorSchoolIds.Contains(schoolId))
        {
            return (false, "Vous ne pouvez pas gérer les matières de cette école.", null);
        }

        var normalizedName = fachName.Trim();
        var normalizedKey = normalizedName.ToUpperInvariant();

        var existing = await dbContext.Faecher
            .FirstOrDefaultAsync(fach => fach.SchoolId == schoolId && fach.Name.ToUpper() == normalizedKey, cancellationToken);
        if (existing is not null)
        {
            return (true, "La matière existe déjà.", existing.Id);
        }

        var created = new Fach
        {
            SchoolId = schoolId,
            Name = normalizedName
        };

        await dbContext.Faecher.AddAsync(created, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, "La matière a été créée.", created.Id);
    }

    public async Task<IReadOnlyList<TeacherAssignmentOption>> GetGemeinsameLehrerSchulenAsync(Guid directorId, Guid teacherId, CancellationToken cancellationToken = default)
    {
        var gemeinsameSchulen = await GetGemeinsameLehrerSchulenMitDirektionAsync(directorId, teacherId, cancellationToken);
        if (gemeinsameSchulen.Count == 0)
        {
            return [];
        }

        return await dbContext.Schools
            .AsNoTracking()
            .Where(school => gemeinsameSchulen.Contains(school.Id))
            .OrderBy(school => school.Name)
            .Select(school => new TeacherAssignmentOption
            {
                TeacherId = school.Id,
                TeacherName = school.Name,
                Email = string.Empty,
                IsAssigned = true
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<List<Guid>> GetGemeinsameLehrerSchulenMitDirektionAsync(Guid directorId, Guid teacherId, CancellationToken cancellationToken)
    {
        var schoolIds = await GetDirectorSchoolIdsAsync(directorId, cancellationToken);
        if (schoolIds.Count == 0)
        {
            return [];
        }

        return await dbContext.UserSchools
            .AsNoTracking()
            .Where(link => link.AppUserId == teacherId && schoolIds.Contains(link.SchoolId))
            .Select(link => link.SchoolId)
            .ToListAsync(cancellationToken);
    }

    private async Task<HashSet<string>> GetManageableSchoolUserRolesAsync(Guid managerUserId, CancellationToken cancellationToken)
    {
        var manager = await userManager.FindByIdAsync(managerUserId.ToString());
        if (manager is null)
        {
            return [];
        }

        var roles = await userManager.GetRolesAsync(manager);
        if (roles.Contains(Rollen.SuperAdmin))
        {
            return [Rollen.SchulLeiter, Rollen.Koordinator, Rollen.Lehrer, Rollen.Schueler, Rollen.ProduktOwner, Rollen.SuperAdmin];
        }

        if (roles.Contains(Rollen.SchulLeiter))
        {
            return [Rollen.Koordinator, Rollen.Lehrer, Rollen.Schueler];
        }

        if (roles.Contains(Rollen.Koordinator))
        {
            return [Rollen.Lehrer, Rollen.Schueler];
        }

        return [];
    }

    public async Task<IReadOnlyList<LevelEnrollmentItem>> GetStudentLevelEnrollmentsAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var profile = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(item => item.AppUserId == studentId)
            .Select(item => new
            {
                item.SchoolId,
                item.CurrentKlasseId,
                item.CurrentLevelCode,
                item.School.SubscriptionPriceOverride
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return [];
        }

        var enrollments = await dbContext.LevelEnrollments
            .AsNoTracking()
            .Where(x => x.StudentId == studentId && x.IstAktuell)
            .ToDictionaryAsync(x => x.KlasseId, cancellationToken);
        var closedClassIds = await dbContext.KursNiveauDurchlaeufe
            .AsNoTracking()
            .Where(durchlauf => durchlauf.SchulID == profile.SchoolId && durchlauf.EndDatum != null)
            .Select(durchlauf => durchlauf.KlasseId)
            .ToHashSetAsync(cancellationToken);
        var activeClassIds = await dbContext.KursNiveauDurchlaeufe
            .AsNoTracking()
            .Where(durchlauf => durchlauf.SchulID == profile.SchoolId && durchlauf.EndDatum == null)
            .Select(durchlauf => durchlauf.KlasseId)
            .ToHashSetAsync(cancellationToken);
        var globalSubscriptionPrice = await GetGlobalSubscriptionPriceAsync(cancellationToken);
        var subscriptionPrice = profile.SubscriptionPriceOverride ?? globalSubscriptionPrice;

        var schoolCourses = await dbContext.Klassen
            .AsNoTracking()
            .Where(course => course.SchoolId == profile.SchoolId)
            .Select(course => new { course.Id, course.Code, course.Title, course.Price, course.SchoolId })
            .ToListAsync(cancellationToken);

        var historicalCourseIds = enrollments.Keys.Except(schoolCourses.Select(course => course.Id)).ToList();
        var historicalCourses = await dbContext.Klassen
            .AsNoTracking()
            .Where(course => historicalCourseIds.Contains(course.Id))
            .Select(course => new { course.Id, course.Code, course.Title, course.Price, course.SchoolId })
            .ToListAsync(cancellationToken);
        var courses = schoolCourses.Concat(historicalCourses)
            .OrderBy(course => course.SchoolId == profile.SchoolId ? 0 : 1)
            .ThenBy(course => course.Code)
            .ToList();

        return courses
            .Select(course =>
            {
                enrollments.TryGetValue(course.Id, out var enrollment);
                var hasActiveClassCycle = activeClassIds.Contains(course.Id);
                var isClassCycleClosed = !hasActiveClassCycle && closedClassIds.Contains(course.Id);
                return new LevelEnrollmentItem
                {
                    KlasseId = course.Id,
                    CourseCode = course.Code,
                    CourseTitle = course.Title,
                    Price = course.Price,
                    SubscriptionPrice = subscriptionPrice,
                    AboPreis = enrollment is not null && enrollment.PriceAtEnrollment > 0m
                        ? enrollment.PriceAtEnrollment
                        : subscriptionPrice,
                    AboTyp = enrollment?.AboTyp ?? DataModels.Schools.AboTyp.SkoulouApp,
                    IsUnlocked = enrollment?.IsUnlocked ?? false,
                    IsPaid = enrollment?.IsPaid ?? false,
                    IstAbgerechnet = enrollment?.AbgerechnetInPeriodeId is not null,
                    UnlockedAtUtc = enrollment?.UnlockedAtUtc,
                    PaidAtUtc = enrollment?.PaidAtUtc,
                    HasActiveClassCycle = hasActiveClassCycle,
                    IsClassCycleClosed = isClassCycleClosed,
                    IsCurrentKlasse = course.Id == profile.CurrentKlasseId
                        || (profile.CurrentKlasseId is null
                            && string.Equals(course.Code, profile.CurrentLevelCode, StringComparison.OrdinalIgnoreCase))
                };
            })
            .ToList();
    }

    public async Task SetLevelUnlockedAsync(Guid studentId, Guid klasseId, bool isUnlocked, CancellationToken cancellationToken = default)
    {
        var enrollment = await dbContext.LevelEnrollments
            .FirstOrDefaultAsync(x => x.StudentId == studentId && x.KlasseId == klasseId && x.IstAktuell, cancellationToken);

        if (enrollment is null)
        {
            var course = await dbContext.Klassen.FirstOrDefaultAsync(x => x.Id == klasseId, cancellationToken);
            if (course is null)
            {
                return;
            }

            // Neue Freischaltungen nur innerhalb der Niveaugruppe des Schuelers zulassen.
            if (isUnlocked && !await _niveauGruppen.IstKlasseErlaubtAsync(studentId, klasseId, cancellationToken))
            {
                throw new InvalidOperationException(NiveauGruppenService.NichtErlaubtMeldung);
            }

            enrollment = new LevelEnrollment
            {
                StudentId = studentId,
                KlasseId = klasseId,
                PriceAtEnrollment = course.Price
            };
            await dbContext.LevelEnrollments.AddAsync(enrollment, cancellationToken);
        }

        enrollment.IsUnlocked = isUnlocked;
        enrollment.UnlockedAtUtc = isUnlocked ? DateTime.UtcNow : enrollment.UnlockedAtUtc;
        enrollment.GeaendertAmUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetLevelPaidAsync(Guid studentId, Guid klasseId, bool isPaid, CancellationToken cancellationToken = default)
    {
        var enrollment = await dbContext.LevelEnrollments
            .FirstOrDefaultAsync(x => x.StudentId == studentId && x.KlasseId == klasseId && x.IstAktuell, cancellationToken);

        if (enrollment is null)
        {
            var course = await dbContext.Klassen.FirstOrDefaultAsync(x => x.Id == klasseId, cancellationToken);
            if (course is null)
            {
                return;
            }

            enrollment = new LevelEnrollment
            {
                StudentId = studentId,
                KlasseId = klasseId,
                PriceAtEnrollment = course.Price
            };
            await dbContext.LevelEnrollments.AddAsync(enrollment, cancellationToken);
        }

        enrollment.IsPaid = isPaid;
        enrollment.PaidAtUtc = isPaid ? DateTime.UtcNow : enrollment.PaidAtUtc;
        enrollment.GeaendertAmUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Liefert für einen Lehrer alle möglichen Niveau-Zuordnungen innerhalb der Schulen des
    /// Direktors und markiert, welche davon aktuell aktiv sind. Der Lehrer muss dem Direktor
    /// über mindestens eine gemeinsame Schule zugeordnet sein.
    /// </summary>
    public async Task<IReadOnlyList<LehrerNiveauZuordnungItem>> GetLehrerNiveauZuordnungenAsync(Guid directorId, Guid teacherId, CancellationToken cancellationToken = default)
    {
        var schoolIds = await GetDirectorSchoolIdsAsync(directorId, cancellationToken);
        if (schoolIds.Count == 0)
        {
            return [];
        }

        // Nur Schulen, denen der Lehrer tatsächlich zugeordnet ist.
        var lehrerSchulIds = await dbContext.UserSchools
            .AsNoTracking()
            .Where(link => link.AppUserId == teacherId && schoolIds.Contains(link.SchoolId))
            .Select(link => link.SchoolId)
            .ToListAsync(cancellationToken);

        if (lehrerSchulIds.Count == 0)
        {
            return [];
        }

        var schulen = await dbContext.Schools
            .AsNoTracking()
            .Where(s => lehrerSchulIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(cancellationToken);

        var niveaus = await dbContext.Klassen
            .AsNoTracking()
            .OrderBy(c => c.Code)
            .Select(c => new { c.Id, c.Code, c.Title, c.SchoolId, c.IstSystemNiveau })
            .ToListAsync(cancellationToken);

        var aktive = (await dbContext.LehrerNiveauZuordnungen
            .AsNoTracking()
            .Where(z => z.LehrerID == teacherId && lehrerSchulIds.Contains(z.SchulID))
            .Select(z => new { z.SchulID, z.KlasseId, z.Wochenstunden })
            .ToListAsync(cancellationToken))
            .ToDictionary(z => z.SchulID + "|" + z.KlasseId, z => z.Wochenstunden);

        var result = new List<LehrerNiveauZuordnungItem>();
        foreach (var schule in schulen.OrderBy(s => s.Name))
        {
            foreach (var niveau in niveaus.Where(niveau => niveau.SchoolId == schule.Id || (niveau.SchoolId == null && niveau.IstSystemNiveau)))
            {
                result.Add(new LehrerNiveauZuordnungItem
                {
                    SchulID = schule.Id,
                    SchulName = schule.Name,
                    KlasseId = niveau.Id,
                    NiveauCode = niveau.Code,
                    NiveauTitel = niveau.Title,
                    IstZugeordnet = aktive.ContainsKey(schule.Id + "|" + niveau.Id),
                    Wochenstunden = aktive.GetValueOrDefault(schule.Id + "|" + niveau.Id, 1m)
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Speichert die vom Direktor gewählten Klassen-Zuordnungen für einen Lehrer. Es werden nur
    /// Zuordnungen innerhalb der Schulen berücksichtigt, die der Direktor verwaltet und denen der
    /// Lehrer zugeordnet ist. Bestehende Zuordnungen dieser Schulen werden ersetzt.
    /// </summary>
    public async Task SetLehrerNiveauZuordnungenAsync(Guid directorId, Guid teacherId, IReadOnlyList<(Guid SchulID, Guid KlasseId, decimal Wochenstunden)> zuordnungen, CancellationToken cancellationToken = default)
    {
        var schoolIds = await GetDirectorSchoolIdsAsync(directorId, cancellationToken);
        if (schoolIds.Count == 0)
        {
            return;
        }

        var lehrerSchulIds = await dbContext.UserSchools
            .AsNoTracking()
            .Where(link => link.AppUserId == teacherId && schoolIds.Contains(link.SchoolId))
            .Select(link => link.SchoolId)
            .ToListAsync(cancellationToken);

        if (lehrerSchulIds.Count == 0)
        {
            return;
        }

        var gueltigeKlassen = await dbContext.Klassen
            .AsNoTracking()
            .Select(c => new { c.Id, c.SchoolId, c.IstSystemNiveau })
            .ToListAsync(cancellationToken);

        var bestehende = await dbContext.LehrerNiveauZuordnungen
            .Where(z => z.LehrerID == teacherId && lehrerSchulIds.Contains(z.SchulID))
            .ToListAsync(cancellationToken);

        dbContext.LehrerNiveauZuordnungen.RemoveRange(bestehende);

        var gewuenscht = zuordnungen
            .Where(z => z.Wochenstunden > 0m && lehrerSchulIds.Contains(z.SchulID) && gueltigeKlassen.Any(klasse => klasse.Id == z.KlasseId && (klasse.SchoolId == z.SchulID || (klasse.SchoolId == null && klasse.IstSystemNiveau))))
            .Distinct()
            .ToList();

        foreach (var (schulId, klasseId, wochenstunden) in gewuenscht)
        {
            await dbContext.LehrerNiveauZuordnungen.AddAsync(new LehrerNiveauZuordnung
            {
                LehrerID = teacherId,
                SchulID = schulId,
                KlasseId = klasseId,
                Wochenstunden = Math.Max(1m, wochenstunden)
            }, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
