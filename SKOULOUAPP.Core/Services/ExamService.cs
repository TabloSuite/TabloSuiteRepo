using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Finanzen;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
using HalloApp.Core.Interfaces;
using HalloApp.Core.Models.Admin;
using HalloApp.Core.Models.Exams;
using HalloApp.Core.Models.Student;
using Microsoft.EntityFrameworkCore;

namespace HalloApp.Core.Services;

public class ExamService(IHalloAppDbContext dbContext)
{
    public async Task<IReadOnlyList<ExamListItem>> GetExamsForStudentAsync(
        Guid studentId,
        string? levelCode = null,
        CancellationToken cancellationToken = default)
    {
        var klasseId = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(x => x.AppUserId == studentId)
            .Select(x => x.CurrentKlasseId)
            .FirstOrDefaultAsync(cancellationToken);
        if (!klasseId.HasValue)
        {
            return [];
        }

        var hasActiveSubscription = await dbContext.LevelEnrollments
            .AsNoTracking()
            .AnyAsync(enrollment => enrollment.StudentId == studentId && enrollment.IsUnlocked, cancellationToken);
        if (!hasActiveSubscription)
        {
            return [];
        }

        var query = dbContext.Exams
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.Questions)
            .Where(x => x.KlasseId == klasseId.Value && x.IsPublishedToStudents)
            .AsQueryable();

        var exams = await query
            .OrderBy(x => x.Klasse.Code)
            .ThenBy(x => x.Title)
            .ToListAsync(cancellationToken);

        var examIds = exams.Select(x => x.Id).ToList();

        var attempts = await dbContext.ExamAttempts
            .AsNoTracking()
            .Where(x => x.StudentId == studentId && x.IsCompleted && examIds.Contains(x.ExamId))
            .ToListAsync(cancellationToken);

        return exams
            .Select(exam =>
            {
                var examAttempts = attempts.Where(a => a.ExamId == exam.Id).ToList();
                return new ExamListItem
                {
                    ExamId = exam.Id,
                    Title = exam.Title,
                    Description = exam.Description,
                    CourseCode = exam.Klasse.Code,
                    DurationInMinutes = exam.DurationInMinutes,
                    PassingScore = exam.PassingScore,
                    QuestionCount = exam.Questions.Count,
                    HasAttempt = examAttempts.Count > 0,
                    IsPassed = examAttempts.Any(a => a.IsPassed),
                    BestScorePercent = examAttempts.Count > 0 ? examAttempts.Max(a => a.ScorePercent) : 0
                };
            })
            .ToList();
    }

    public async Task<ExamSessionViewModel?> StartExamAsync(
        Guid examId,
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        var exam = await dbContext.Exams
            .Include(x => x.Klasse)
            .Include(x => x.Questions)
                .ThenInclude(x => x.Options)
            .FirstOrDefaultAsync(x => x.Id == examId, cancellationToken);

        if (exam is null || exam.Questions.Count == 0)
        {
            return null;
        }

        var isAssignedToCurrentClass = await dbContext.StudentProfiles
            .AsNoTracking()
            .AnyAsync(x => x.AppUserId == studentId
                && x.CurrentKlasseId == exam.KlasseId, cancellationToken);
        var hasActiveSubscription = await dbContext.LevelEnrollments
            .AsNoTracking()
            .AnyAsync(enrollment => enrollment.StudentId == studentId && enrollment.KlasseId == exam.KlasseId && enrollment.IsUnlocked, cancellationToken);
        if (!exam.IsPublishedToStudents || !isAssignedToCurrentClass || !hasActiveSubscription)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var attempt = new ExamAttempt
        {
            ExamId = examId,
            StudentId = studentId,
            StartedAtUtc = now,
            DeadlineUtc = now.AddMinutes(exam.DurationInMinutes),
            TotalQuestions = exam.Questions.Count,
            IsCompleted = false
        };

        await dbContext.ExamAttempts.AddAsync(attempt, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ExamSessionViewModel
        {
            AttemptId = attempt.Id,
            ExamId = exam.Id,
            Title = exam.Title,
            Description = exam.Description,
            CourseCode = exam.Klasse.Code,
            DurationInMinutes = exam.DurationInMinutes,
            PassingScore = exam.PassingScore,
            DeadlineUtc = attempt.DeadlineUtc,
            Questions = exam.Questions
                .OrderBy(q => q.Order)
                .Select(q => new ExamQuestionViewModel
                {
                    QuestionId = q.Id,
                    Prompt = q.Prompt,
                    Order = q.Order,
                    QuestionType = MapExamType(q.QuestionType),
                    AudioText = q.AudioText,
                        TtsLanguage = string.IsNullOrWhiteSpace(q.TtsLanguage) ? "de-DE" : q.TtsLanguage,
                    GapCount = AnswerMatchingService.ParseGapAnswers(q.GapAnswersJson).Count,
                    Points = q.Points,
                    Options = q.Options
                        .OrderBy(o => o.Order)
                        .Select(o => new ExamOptionViewModel
                        {
                            OptionId = o.Id,
                            Text = o.Text,
                            Order = o.Order
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    public async Task<ExamResult> SubmitExamAsync(
        Guid attemptId,
        Guid studentId,
        IReadOnlyDictionary<Guid, Guid> answers,
        CancellationToken cancellationToken = default)
    {
        var submissions = answers
            .Select(kvp => new ExamAnswerSubmission { QuestionId = kvp.Key, SelectedOptionId = kvp.Value })
            .ToList();
        return await SubmitExamAsync(attemptId, studentId, submissions, cancellationToken);
    }

    /// <summary>
    /// Generischer Prüfungs-Submit für Choice-, Text-, Satzbau-, Audio- und Lückentext-Fragen.
    /// Automatische Bewertung bei Choice/GapFill/Musterlösung; Text ohne Musterlösung wird manuell benotet.
    /// </summary>
    public async Task<ExamResult> SubmitExamAsync(
        Guid attemptId,
        Guid studentId,
        IReadOnlyList<ExamAnswerSubmission> submissions,
        CancellationToken cancellationToken = default)
    {
        var attempt = await dbContext.ExamAttempts
            .FirstOrDefaultAsync(x => x.Id == attemptId && x.StudentId == studentId, cancellationToken);

        if (attempt is null)
        {
            return new ExamResult { Succeeded = false, Message = "La session d'examen est introuvable." };
        }

        if (attempt.IsCompleted)
        {
            return new ExamResult
            {
                Succeeded = true,
                Message = "Diese Prüfung wurde bereits abgeschlossen.",
                CorrectAnswers = attempt.CorrectAnswers,
                TotalQuestions = attempt.TotalQuestions,
                ScorePercent = attempt.ScorePercent,
                IsPassed = attempt.IsPassed,
                RequiresManualGrading = attempt.RequiresManualGrading
            };
        }

        var exam = await dbContext.Exams
            .Include(x => x.Questions)
                .ThenInclude(x => x.Options)
            .FirstOrDefaultAsync(x => x.Id == attempt.ExamId, cancellationToken);

        if (exam is null)
        {
            return new ExamResult { Succeeded = false, Message = "L'examen est introuvable." };
        }

        var now = DateTime.UtcNow;
        var timedOut = now > attempt.DeadlineUtc.AddSeconds(5);

        var submissionByQuestion = submissions
            .GroupBy(s => s.QuestionId)
            .ToDictionary(g => g.Key, g => g.Last());

        var totalQuestions = exam.Questions.Count;
        var correctAnswers = 0;
        var requiresManualGrading = false;

        foreach (var question in exam.Questions)
        {
            submissionByQuestion.TryGetValue(question.Id, out var submission);

            var answer = new ExamAnswer
            {
                ExamAttemptId = attempt.Id,
                ExamQuestionId = question.Id
            };

            switch (question.QuestionType)
            {
                case ExamQuestionType.GapFill:
                {
                    var gapValuesJson = AnswerMatchingService.SerializeStudentGapValues(submission?.GapValues ?? []);
                    answer.GapAnswersJson = gapValuesJson;
                    var (correct, total) = AnswerMatchingService.GradeGaps(question.GapAnswersJson, gapValuesJson);
                    answer.IsCorrect = total > 0 && correct == total;
                    break;
                }

                case ExamQuestionType.SingleChoice:
                case ExamQuestionType.AudioSingleChoice:
                {
                    answer.SelectedOptionId = submission?.SelectedOptionId;
                    var selected = submission?.SelectedOptionId is Guid sel
                        ? question.Options.FirstOrDefault(o => o.Id == sel)
                        : null;
                    answer.IsCorrect = selected is not null && selected.IsCorrect;
                    break;
                }

                case ExamQuestionType.MultipleChoice:
                case ExamQuestionType.AudioMultipleChoice:
                {
                    var selectedIds = (submission?.SelectedOptionIds ?? [])
                        .Where(id => question.Options.Any(o => o.Id == id))
                        .Distinct()
                        .ToList();
                    answer.SelectedOptionIdsJson = System.Text.Json.JsonSerializer.Serialize(selectedIds);
                    var correctIds = question.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
                    // Alles-oder-nichts: genau die korrekten Optionen mussen gewahlt sein.
                    answer.IsCorrect = correctIds.Count > 0 && correctIds.SetEquals(selectedIds);
                    break;
                }

                default:
                {
                    // Text-basierte Typen: FreeText, SentenceBuilding, AudioText
                    answer.FreeTextAnswer = submission?.TextAnswer?.Trim();
                    var accepted = AnswerMatchingService.ParseAcceptedAnswers(question.AcceptedAnswersJson);
                    if (accepted.Count > 0)
                    {
                        answer.IsCorrect = AnswerMatchingService.Matches(answer.FreeTextAnswer, accepted);
                    }
                    else
                    {
                        answer.IsManuallyGraded = true;
                        requiresManualGrading = true;
                    }
                    break;
                }
            }

            if (answer.IsCorrect)
            {
                correctAnswers++;
            }

            await dbContext.ExamAnswers.AddAsync(answer, cancellationToken);
        }

        var scorePercent = totalQuestions == 0
            ? 0
            : (int)Math.Round(correctAnswers * 100.0 / totalQuestions);
        var isPassed = !timedOut && !requiresManualGrading && scorePercent >= exam.PassingScore;

        attempt.SubmittedAtUtc = now;
        attempt.CorrectAnswers = correctAnswers;
        attempt.TotalQuestions = totalQuestions;
        attempt.ScorePercent = scorePercent;
        attempt.IsPassed = isPassed;
        attempt.IsCompleted = true;
        attempt.RequiresManualGrading = requiresManualGrading;
        attempt.GeaendertAmUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        string message;
        if (timedOut)
        {
            message = "Le temps est écoulé. Vos réponses ont été évaluées.";
        }
        else if (requiresManualGrading)
        {
            message = "Vos réponses ont été enregistrées. Certaines doivent encore être évaluées par votre enseignant.";
        }
        else
        {
            message = isPassed ? "Réussi ! Félicitations." : "Vous n'avez pas réussi. Réessayez.";
        }

        return new ExamResult
        {
            Succeeded = true,
            Message = message,
            CorrectAnswers = correctAnswers,
            TotalQuestions = totalQuestions,
            ScorePercent = scorePercent,
            PassingScore = exam.PassingScore,
            IsPassed = isPassed,
            TimedOut = timedOut,
            RequiresManualGrading = requiresManualGrading
        };
    }

    // ---------------------------------------------------------------------
    // Authoring (Lehrer/Schulleiter): Erstellen, Bearbeiten, Löschen, PDF
    // ---------------------------------------------------------------------

    /// <summary>
    /// Legt eine neue Prüfung an oder aktualisiert eine bestehende inklusive
    /// Aufgabenblöcken (ExamExercise) und Aufgaben (ExamQuestion).
    /// </summary>
    public async Task<Guid> SaveExamAsync(Guid authorId, ExamFormModel model, CancellationToken cancellationToken = default)
    {
        ValidateExamForm(model);

        var niveauCode = model.NiveauCode.Trim();
        var klasse = await dbContext.Klassen
            .FirstOrDefaultAsync(x => x.Code == niveauCode, cancellationToken)
            ?? throw new InvalidOperationException("Le niveau sélectionné est introuvable.");
        if (klasse.IstSystemNiveau)
        {
            throw new InvalidOperationException("Les niveaux standard font partie du matériel pédagogique et ne peuvent pas être gérés par les enseignants.");
        }

        Exam exam;
        if (model.ExamId.HasValue)
        {
            exam = await dbContext.Exams
                .Include(x => x.Exercises)
                    .ThenInclude(x => x.Questions)
                        .ThenInclude(x => x.Options)
                .Include(x => x.Questions)
                    .ThenInclude(x => x.Options)
                .FirstOrDefaultAsync(x => x.Id == model.ExamId.Value && x.AuthorId == authorId && !x.Klasse.IstSystemNiveau, cancellationToken)
                ?? throw new InvalidOperationException("L'examen est introuvable.");

            // Bestehende Fragen/Blöcke entfernen und neu aufbauen.
            dbContext.ExamQuestions.RemoveRange(exam.Questions);
            dbContext.ExamExercises.RemoveRange(exam.Exercises);
            exam.Questions.Clear();
            exam.Exercises.Clear();
        }
        else
        {
            exam = new Exam { AuthorId = authorId };
            await dbContext.Exams.AddAsync(exam, cancellationToken);
        }

        exam.KlasseId = klasse.Id;
        exam.Title = model.Title.Trim();
        exam.Description = Text.HtmlSanitizer.Sanitize(model.Description);
        exam.DurationInMinutes = model.DurationInMinutes;
        exam.PassingScore = model.PassingScore;
        exam.ExamType = ExamType.TestExam;
        exam.IsPublishedToStudents = model.ReleaseTarget == ExamReleaseTarget.ClassStudents;
        exam.IsSharedWithSchool = model.ReleaseTarget == ExamReleaseTarget.SchoolLeader;
        exam.IsPdfExportEnabled = model.IsPdfExportEnabled;
        exam.GeaendertAmUtc = DateTime.UtcNow;

        var globalOrder = 1;
        var exerciseOrder = 1;
        foreach (var exerciseModel in model.Exercises)
        {
            var exercise = new ExamExercise
            {
                Title = string.IsNullOrWhiteSpace(exerciseModel.Title)
                    ? $"Exercice {exerciseOrder}"
                    : exerciseModel.Title.Trim(),
                Consigne = Text.HtmlSanitizer.SanitizeMitBildern(exerciseModel.Consigne),
                Order = exerciseOrder++
            };

            foreach (var questionModel in exerciseModel.Questions)
            {
                var question = new ExamQuestion
                {
                    Exam = exam,
                    Prompt = Text.HtmlSanitizer.SanitizeMitBildern(questionModel.Prompt),
                    QuestionType = questionModel.QuestionType,
                    Points = questionModel.Points,
                    Order = globalOrder++,
                    AudioText = string.IsNullOrWhiteSpace(questionModel.AudioText) ? null : questionModel.AudioText.Trim(),
                    TtsLanguage = NormalizeTtsLanguage(questionModel.TtsLanguage)
                };

                if (IsChoiceType(questionModel.QuestionType))
                {
                    var optionOrder = 1;
                    foreach (var optionModel in questionModel.Options)
                    {
                        question.Options.Add(new ExamQuestionOption
                        {
                            Text = optionModel.Text.Trim(),
                            IsCorrect = optionModel.IsCorrect,
                            Order = optionOrder++
                        });
                    }
                }
                else if (questionModel.QuestionType == ExamQuestionType.GapFill)
                {
                    question.GapAnswersJson = AnswerMatchingService.SerializeGaps(
                        questionModel.Gaps.Select(g => AnswerMatchingService.SplitVariants(g.AcceptedAnswers)));
                }
                else
                {
                    question.AcceptedAnswersJson = AnswerMatchingService.SerializeVariants(
                        AnswerMatchingService.SplitVariants(questionModel.AcceptedAnswers));
                }

                exercise.Questions.Add(question);
                exam.Questions.Add(question);
            }

            exam.Exercises.Add(exercise);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return exam.Id;
    }

    /// <summary>Prüfungen für die Lehrer-/Schulleiterverwaltung.</summary>
    public async Task<IReadOnlyList<ExamManagementItem>> GetTeacherExamsAsync(Guid authorId, CancellationToken cancellationToken = default)
    {
        var exams = await dbContext.Exams
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.Exercises)
                .ThenInclude(x => x.Questions)
            .Include(x => x.Questions)
            .Include(x => x.Attempts)
            .Where(x => x.AuthorId == authorId && !x.Klasse.IstSystemNiveau)
            .OrderByDescending(x => x.ErstelltAmUtc)
            .ToListAsync(cancellationToken);

        return exams
            .Select(exam => new ExamManagementItem
            {
                ExamId = exam.Id,
                Title = exam.Title,
                NiveauCode = exam.Klasse.Code,
                DurationInMinutes = exam.DurationInMinutes,
                PassingScore = exam.PassingScore,
                ExerciseCount = exam.Exercises.Count,
                QuestionCount = exam.Questions.Count,
                AttemptCount = exam.Attempts.Count,
                ExamType = exam.ExamType,
                ReleaseTarget = exam.IsPublishedToStudents
                    ? ExamReleaseTarget.ClassStudents
                    : exam.IsSharedWithSchool
                        ? ExamReleaseTarget.SchoolLeader
                        : ExamReleaseTarget.PrivateAuthor
            })
            .ToList();
    }

    /// <summary>Lädt eine Prüfung als bearbeitbares Formmodell.</summary>
    public async Task<ExamFormModel?> GetExamForEditAsync(Guid authorId, Guid examId, CancellationToken cancellationToken = default)
    {
        var exam = await dbContext.Exams
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.Exercises)
                .ThenInclude(x => x.Questions)
                    .ThenInclude(x => x.Options)
            .Include(x => x.Questions)
                .ThenInclude(x => x.Options)
            .FirstOrDefaultAsync(x => x.Id == examId && x.AuthorId == authorId && !x.Klasse.IstSystemNiveau, cancellationToken);

        if (exam is null)
        {
            return null;
        }

        return new ExamFormModel
        {
            ExamId = exam.Id,
            Title = exam.Title,
            Description = exam.Description,
            NiveauCode = exam.Klasse.Code,
            DurationInMinutes = exam.DurationInMinutes,
            PassingScore = exam.PassingScore,
            ExamType = exam.ExamType,
            ReleaseTarget = exam.IsPublishedToStudents
                ? ExamReleaseTarget.ClassStudents
                : exam.IsSharedWithSchool
                    ? ExamReleaseTarget.SchoolLeader
                    : ExamReleaseTarget.PrivateAuthor,
            IsPdfExportEnabled = exam.IsPdfExportEnabled,
            Exercises = BuildExerciseFormModels(exam)
        };
    }

    /// <summary>Löscht eine Prüfung samt Blöcken, Fragen und Versuchen.</summary>
    public async Task<(bool Succeeded, string Message)> DeleteExamAsync(Guid authorId, Guid examId, CancellationToken cancellationToken = default)
    {
        var exam = await dbContext.Exams
            .Include(x => x.Klasse)
            .Include(x => x.Exercises)
            .Include(x => x.Questions)
            .Include(x => x.Attempts)
            .FirstOrDefaultAsync(x => x.Id == examId && x.AuthorId == authorId, cancellationToken);

        if (exam is null)
        {
            return (false, "L'examen est introuvable.");
        }
        if (exam.Klasse.IstSystemNiveau)
        {
            return (false, "Cet examen standard fait partie du matériel pédagogique et ne peut pas être supprimé.");
        }

        dbContext.Exams.Remove(exam);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, "L'examen a été supprimé.");
    }

    /// <summary>
    /// Projektion einer Prüfung für den druckbaren Schüler-Prüfungsbogen (ohne Lösungen).
    /// </summary>
    public async Task<ExamPdfViewModel?> GetExamForPdfAsync(Guid authorId, Guid examId, CancellationToken cancellationToken = default)
    {
        var exam = await dbContext.Exams
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.Exercises)
                .ThenInclude(x => x.Questions)
                    .ThenInclude(x => x.Options)
            .Include(x => x.Questions)
                .ThenInclude(x => x.Options)
            .FirstOrDefaultAsync(x => x.Id == examId && x.AuthorId == authorId, cancellationToken);

        if (exam is null)
        {
            return null;
        }

        var groupedQuestions = exam.Questions.Where(q => q.ExamExerciseId.HasValue).ToList();
        var ungrouped = exam.Questions.Where(q => !q.ExamExerciseId.HasValue).OrderBy(q => q.Order).ToList();

        var pdf = new ExamPdfViewModel
        {
            ExamId = exam.Id,
            Title = exam.Title,
            Description = exam.Description,
            NiveauCode = exam.Klasse.Code,
            DurationInMinutes = exam.DurationInMinutes,
            TotalPoints = exam.Questions.Sum(q => q.Points)
        };

        var blockNumber = 1;
        var questionNumber = 1;

        foreach (var exercise in exam.Exercises.OrderBy(e => e.Order))
        {
            var block = new ExamPdfExercise
            {
                Number = blockNumber++,
                Title = string.IsNullOrWhiteSpace(exercise.Title) ? $"Exercice {blockNumber - 1}" : exercise.Title,
                Consigne = exercise.Consigne
            };

            foreach (var q in groupedQuestions.Where(q => q.ExamExerciseId == exercise.Id).OrderBy(q => q.Order))
            {
                block.Questions.Add(MapPdfQuestion(q, questionNumber++));
            }

            pdf.Exercises.Add(block);
        }

        // Rückwärtskompatibilität: flache (ungruppierte) Fragen als eigener Block.
        if (ungrouped.Count > 0)
        {
            var block = new ExamPdfExercise
            {
                Number = blockNumber,
                Title = pdf.Exercises.Count == 0 ? "Questions" : $"Exercice {blockNumber}"
            };

            foreach (var q in ungrouped)
            {
                block.Questions.Add(MapPdfQuestion(q, questionNumber++));
            }

            pdf.Exercises.Add(block);
        }

        return pdf;
    }

    /// <summary>
    /// Liefert die Abschlussübersicht eines Klassen-Durchlaufs. Pro relevantem Exam wird
    /// der jüngste abgeschlossene Versuch eines Schülers berücksichtigt; eine nachgetragene
    /// Note hat dabei Vorrang vor dem automatisch ermittelten Prozentwert.
    /// </summary>
    public async Task<KlassenAbschlussUebersicht?> GetKlassenAbschlussUebersichtAsync(
        Guid durchlaufId,
        CancellationToken cancellationToken = default)
    {
        var durchlauf = await dbContext.KursNiveauDurchlaeufe
            .AsNoTracking()
            .Include(x => x.Klasse)
            .FirstOrDefaultAsync(x => x.Id == durchlaufId, cancellationToken);
        if (durchlauf is null)
        {
            return null;
        }

        var teilnehmerIds = await GetDurchlaufTeilnehmerIdsAsync(durchlauf, cancellationToken);
        var schueler = await dbContext.Users
            .AsNoTracking()
            .Where(user => teilnehmerIds.Contains(user.Id))
            .Select(user => new { StudentId = user.Id, user.FullName })
            .OrderBy(x => x.FullName)
            .ToListAsync(cancellationToken);

        var entscheidungen = await dbContext.KlassenAbschlussEntscheidungen
            .AsNoTracking()
            .Where(x => x.DurchlaufId == durchlaufId)
            .ToDictionaryAsync(x => x.StudentId, cancellationToken);

        return new KlassenAbschlussUebersicht
        {
            DurchlaufId = durchlauf.Id,
            KlasseId = durchlauf.KlasseId,
            KlasseCode = durchlauf.Klasse.Code,
            IstAktiv = durchlauf.IstAktiv,
            Schueler = schueler.Select(student =>
            {
                entscheidungen.TryGetValue(student.StudentId, out var entscheidung);
                return new KlassenAbschlussSchueler
                {
                    StudentId = student.StudentId,
                    Name = student.FullName,
                    AbschlussExamAnzahl = 0,
                    AbschlussNoteText = !string.IsNullOrWhiteSpace(entscheidung?.AbschlussNoteText)
                        ? entscheidung!.AbschlussNoteText
                        : entscheidung?.AbschlussNote?.ToString("N2"),
                    IsBestanden = entscheidung?.IsBestanden
                };
            }).ToList()
        };
    }

    /// <summary>Liefert die nachtragbaren Ergebnisse abschlussrelevanter Exams eines aktiven Durchlaufs.</summary>
    public async Task<IReadOnlyList<AbschlussExamErgebnis>> GetAbschlussExamErgebnisseAsync(
        Guid durchlaufId,
        CancellationToken cancellationToken = default)
    {
        var durchlauf = await dbContext.KursNiveauDurchlaeufe
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == durchlaufId, cancellationToken);
        if (durchlauf is null)
        {
            return [];
        }

        var teilnehmerIds = await GetDurchlaufTeilnehmerIdsAsync(durchlauf, cancellationToken);

        return await (
            from attempt in dbContext.ExamAttempts.AsNoTracking()
            join exam in dbContext.Exams.AsNoTracking() on attempt.ExamId equals exam.Id
            join user in dbContext.Users.AsNoTracking() on attempt.StudentId equals user.Id
            where exam.KlasseId == durchlauf.KlasseId
                  && exam.ExamType == ExamType.AbschlussRelevant
                  && attempt.IsCompleted
                  && attempt.SubmittedAtUtc >= durchlauf.StartDatum
                  && (durchlauf.EndDatum == null || attempt.SubmittedAtUtc <= durchlauf.EndDatum)
                  && teilnehmerIds.Contains(attempt.StudentId)
            select new AbschlussExamErgebnis
            {
                AttemptId = attempt.Id,
                StudentId = attempt.StudentId,
                StudentName = user.FullName,
                ExamTitel = exam.Title,
                AutomatischerScorePercent = attempt.ScorePercent,
                ManuelleNote = attempt.ManuelleNote
            })
            .OrderBy(x => x.StudentName)
            .ThenBy(x => x.ExamTitel)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Trägt eine Note zu einem abschlussrelevanten Exam nach, solange der Klassendurchlauf aktiv ist.</summary>
    public async Task<(bool Succeeded, string Message)> SetManuelleAbschlussExamNoteAsync(
        Guid attemptId,
        decimal note,
        Guid erfasstVonUserId,
        CancellationToken cancellationToken = default)
    {
        if (note is < 0m or > 100m)
        {
            return (false, "La note doit être comprise entre 0 et 100.");
        }

        var attempt = await dbContext.ExamAttempts
            .Include(x => x.Exam)
            .FirstOrDefaultAsync(x => x.Id == attemptId, cancellationToken);
        if (attempt is null || !attempt.IsCompleted || attempt.Exam.ExamType != ExamType.AbschlussRelevant)
        {
            return (false, "Le résultat sélectionné n'est pas une épreuve finale valide.");
        }

        var durchlaufIstAktiv = await dbContext.KursNiveauDurchlaeufe.AnyAsync(
            x => x.KlasseId == attempt.Exam.KlasseId && x.EndDatum == null,
            cancellationToken);
        if (!durchlaufIstAktiv)
        {
            return (false, "La classe est clôturée. Les notes ne peuvent plus être modifiées.");
        }

        attempt.ManuelleNote = note;
        attempt.ManuelleNoteErfasstAmUtc = DateTime.UtcNow;
        attempt.ManuelleNoteErfasstVonUserId = erfasstVonUserId;
        attempt.GeaendertAmUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, "La note finale a été enregistrée.");
    }

    /// <summary>Setzt die vom Notendurchschnitt unabhängige Bestanden-Entscheidung für einen Schüler.</summary>
    public async Task<(bool Succeeded, string Message)> SetKlassenBestandenEntscheidungAsync(
        Guid durchlaufId,
        Guid studentId,
        bool isBestanden,
        Guid entscheidetVonUserId,
        CancellationToken cancellationToken = default)
    {
        var bericht = await GetKlassenAbschlussUebersichtAsync(durchlaufId, cancellationToken);
        if (bericht is null)
        {
            return (false, "Le cycle est introuvable.");
        }
        if (!bericht.IstAktiv)
        {
            return (false, "Le cycle est déjà clôturé.");
        }

        var schueler = bericht.Schueler.FirstOrDefault(x => x.StudentId == studentId);
        if (schueler is null)
        {
            return (false, "L'élève n'appartient pas à cette classe.");
        }

        var entscheidung = await dbContext.KlassenAbschlussEntscheidungen
            .FirstOrDefaultAsync(x => x.DurchlaufId == durchlaufId && x.StudentId == studentId, cancellationToken);
        if (entscheidung is null)
        {
            entscheidung = new KlassenAbschlussEntscheidung { DurchlaufId = durchlaufId, StudentId = studentId };
            await dbContext.KlassenAbschlussEntscheidungen.AddAsync(entscheidung, cancellationToken);
        }

        entscheidung.AbschlussNoteText ??= schueler.AbschlussNoteText;
        entscheidung.IsBestanden = isBestanden;
        entscheidung.EntscheidetAmUtc = DateTime.UtcNow;
        entscheidung.EntscheidetVonUserId = entscheidetVonUserId;
        entscheidung.GeaendertAmUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, "La décision de réussite a été enregistrée.");
    }

    /// <summary>
    /// Versetzt ausgewählte, als bestanden entschiedene Schüler nach Abschluss eines
    /// Durchlaufs in eine vorhandene Zielklasse derselben Schule.
    /// </summary>
    public async Task<(bool Succeeded, string Message)> VersetzeSchuelerAsync(
        Guid durchlaufId,
        IReadOnlyCollection<Guid> studentIds,
        Guid zielKlasseId,
        CancellationToken cancellationToken = default)
    {
        if (studentIds.Count == 0)
        {
            return (false, "Sélectionnez au moins un élève.");
        }

        var durchlauf = await dbContext.KursNiveauDurchlaeufe
            .Include(x => x.Klasse)
            .FirstOrDefaultAsync(x => x.Id == durchlaufId, cancellationToken);
        if (durchlauf is null || durchlauf.EndDatum is null)
        {
            return (false, "La classe doit être clôturée avant la promotion.");
        }

        var zielKlasse = await dbContext.Klassen.FirstOrDefaultAsync(
            x => x.Id == zielKlasseId && x.SchoolId == durchlauf.SchulID && !x.IstSystemNiveau,
            cancellationToken);
        if (zielKlasse is null || zielKlasse.Id == durchlauf.KlasseId)
        {
            return (false, "Choisissez une classe cible valide de la même école.");
        }

        var entscheidungen = await dbContext.KlassenAbschlussEntscheidungen
            .Where(x => x.DurchlaufId == durchlaufId && studentIds.Contains(x.StudentId))
            .ToListAsync(cancellationToken);
        if (entscheidungen.Count != studentIds.Count || entscheidungen.Any(x => x.IsBestanden != true))
        {
            return (false, "Seuls les élèves marqués comme réussis peuvent être promus.");
        }

        var profile = await dbContext.StudentProfiles
            .Where(x => studentIds.Contains(x.AppUserId) && x.SchoolId == durchlauf.SchulID)
            .ToListAsync(cancellationToken);
        if (profile.Count != studentIds.Count)
        {
            return (false, "Un élève sélectionné n'appartient pas à cette école.");
        }

        var vorhandeneEinschreibungen = await dbContext.LevelEnrollments
            .Where(x => studentIds.Contains(x.StudentId) && x.KlasseId == zielKlasseId)
            .Select(x => x.StudentId)
            .ToListAsync(cancellationToken);
        foreach (var student in profile)
        {
            student.CurrentKlasseId = zielKlasse.Id;
            student.CurrentLevelCode = zielKlasse.Code;
            student.GeaendertAmUtc = DateTime.UtcNow;
            if (!vorhandeneEinschreibungen.Contains(student.AppUserId))
            {
                await dbContext.LevelEnrollments.AddAsync(new LevelEnrollment
                {
                    StudentId = student.AppUserId,
                    KlasseId = zielKlasse.Id
                }, cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, "Les élèves sélectionnés ont été promus.");
    }

    private async Task<List<Guid>> GetDurchlaufTeilnehmerIdsAsync(
        KursNiveauDurchlauf durchlauf,
        CancellationToken cancellationToken)
    {
        if (durchlauf.EndDatum is null)
        {
            return await dbContext.StudentProfiles
                .AsNoTracking()
                .Where(profile => profile.SchoolId == durchlauf.SchulID && profile.CurrentKlasseId == durchlauf.KlasseId)
                .Select(profile => profile.AppUserId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        var entschiedeneStudenten = await dbContext.KlassenAbschlussEntscheidungen
            .AsNoTracking()
            .Where(entscheidung => entscheidung.DurchlaufId == durchlauf.Id)
            .Select(entscheidung => entscheidung.StudentId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (entschiedeneStudenten.Count > 0)
        {
            return entschiedeneStudenten;
        }

        return await (
            from enrollment in dbContext.LevelEnrollments.AsNoTracking()
            join profile in dbContext.StudentProfiles.AsNoTracking() on enrollment.StudentId equals profile.AppUserId
            where enrollment.KlasseId == durchlauf.KlasseId && profile.SchoolId == durchlauf.SchulID
            select enrollment.StudentId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private static ExamPdfQuestion MapPdfQuestion(ExamQuestion q, int number) => new()
    {
        Number = number,
        Prompt = q.Prompt,
        Points = q.Points,
        QuestionType = MapExamType(q.QuestionType),
        GapCount = AnswerMatchingService.ParseGapAnswers(q.GapAnswersJson).Count,
        Options = q.Options.OrderBy(o => o.Order).Select(o => o.Text).ToList(),
        CorrectOptions = q.Options.Where(o => o.IsCorrect).OrderBy(o => o.Order).Select(o => o.Text).ToList(),
        AcceptedAnswers = string.Join(Environment.NewLine, AnswerMatchingService.ParseAcceptedAnswers(q.AcceptedAnswersJson)),
        GapSolutions = AnswerMatchingService.ParseGapAnswers(q.GapAnswersJson)
            .Select(gap => string.Join(" / ", gap))
            .ToList()
    };

    private static List<ExamExerciseFormModel> BuildExerciseFormModels(Exam exam)
    {
        var questionsByExercise = exam.Questions
            .Where(q => q.ExamExerciseId.HasValue)
            .GroupBy(q => q.ExamExerciseId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var models = exam.Exercises
            .OrderBy(e => e.Order)
            .Select(e => new ExamExerciseFormModel
            {
                ExerciseId = e.Id,
                Title = e.Title,
                Consigne = e.Consigne,
                Questions = (questionsByExercise.TryGetValue(e.Id, out var qs) ? qs : e.Questions.ToList())
                    .OrderBy(q => q.Order)
                    .Select(MapQuestionFormModel)
                    .ToList()
            })
            .ToList();

        // Ungruppierte Alt-Fragen in einen zusätzlichen Block überführen.
        var ungrouped = exam.Questions.Where(q => !q.ExamExerciseId.HasValue).OrderBy(q => q.Order).ToList();
        if (ungrouped.Count > 0)
        {
            models.Add(new ExamExerciseFormModel
            {
                Title = "Questions",
                Questions = ungrouped.Select(MapQuestionFormModel).ToList()
            });
        }

        return models;
    }

    private static ExamQuestionFormModel MapQuestionFormModel(ExamQuestion q) => new()
    {
        QuestionId = q.Id,
        Prompt = q.Prompt,
        QuestionType = q.QuestionType,
        Points = q.Points,
        AudioText = q.AudioText,
        TtsLanguage = string.IsNullOrWhiteSpace(q.TtsLanguage) ? "de-DE" : q.TtsLanguage,
        AcceptedAnswers = string.Join(
            Environment.NewLine,
            AnswerMatchingService.ParseAcceptedAnswers(q.AcceptedAnswersJson)),
        Gaps = AnswerMatchingService.ParseGapAnswers(q.GapAnswersJson)
            .Select(g => new ExamGapFormModel { AcceptedAnswers = string.Join(Environment.NewLine, g) })
            .ToList(),
        Options = q.Options
            .OrderBy(o => o.Order)
            .Select(o => new ExamQuestionOptionFormModel
            {
                OptionId = o.Id,
                Text = o.Text,
                IsCorrect = o.IsCorrect
            })
            .ToList()
    };

    /// <summary>Auswahl-basierte Fragetypen: SingleChoice, MultipleChoice, AudioSingleChoice, AudioMultipleChoice.</summary>
    internal static bool IsChoiceType(ExamQuestionType type) =>
        type is ExamQuestionType.SingleChoice or ExamQuestionType.MultipleChoice or ExamQuestionType.AudioSingleChoice or ExamQuestionType.AudioMultipleChoice;

    private static string NormalizeTtsLanguage(string? language)
    {
        var value = (language ?? string.Empty).Trim().ToLowerInvariant();
        if (value.StartsWith("fr"))
        {
            return "fr-FR";
        }

        if (value.StartsWith("en"))
        {
            return "en-US";
        }

        return "de-DE";
    }

    private static void ValidateExamForm(ExamFormModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
        {
            throw new InvalidOperationException("Veuillez saisir un titre.");
        }

        if (string.IsNullOrWhiteSpace(model.NiveauCode))
        {
            throw new InvalidOperationException("Veuillez choisir un niveau.");
        }

        if (model.Exercises.Count == 0)
        {
            throw new InvalidOperationException("Veuillez ajouter au moins un exercice.");
        }

        var allQuestions = model.Exercises.SelectMany(e => e.Questions).ToList();
        if (allQuestions.Count == 0)
        {
            throw new InvalidOperationException("Veuillez ajouter au moins une question.");
        }

        foreach (var question in allQuestions)
        {
            if (string.IsNullOrWhiteSpace(question.Prompt))
            {
                throw new InvalidOperationException("Chaque question doit contenir un énoncé.");
            }

            if (question.Points < 1)
            {
                throw new InvalidOperationException("Chaque question doit valoir au moins 1 point.");
            }

            if (IsChoiceType(question.QuestionType) && question.Options.Count < 2)
            {
                throw new InvalidOperationException("Chaque question à choix doit avoir au moins deux options.");
            }
        }
    }

    private static string MapExamType(ExamQuestionType type) => type switch
    {
        ExamQuestionType.SingleChoice => "SingleChoice",
        ExamQuestionType.MultipleChoice => "MultipleChoice",
        ExamQuestionType.AudioSingleChoice => "AudioSingleChoice",
        ExamQuestionType.AudioMultipleChoice => "AudioMultipleChoice",
        ExamQuestionType.GapFill => "GapFill",
        ExamQuestionType.FreeText => "Text",
        ExamQuestionType.SentenceBuilding => "SentenceBuilding",
        ExamQuestionType.AudioText => "AudioText",
        _ => "SingleChoice"
    };

    /// <summary>
    /// Pflegt die manuelle Abschlussnote (Freitext) eines Schülers in einem Durchlauf.
    /// </summary>
    public async Task<(bool Succeeded, string Message)> SetManuelleAbschlussNoteAsync(
        Guid durchlaufId,
        Guid studentId,
        string? abschlussNoteText,
        Guid erfasstVonUserId,
        CancellationToken cancellationToken = default)
    {
        var bericht = await GetKlassenAbschlussUebersichtAsync(durchlaufId, cancellationToken);
        if (bericht is null)
        {
            return (false, "Le cycle est introuvable.");
        }

        var schueler = bericht.Schueler.FirstOrDefault(x => x.StudentId == studentId);
        if (schueler is null)
        {
            return (false, "L'élève n'appartient pas à cette classe.");
        }

        var bereinigteNote = string.IsNullOrWhiteSpace(abschlussNoteText)
            ? null
            : abschlussNoteText.Trim();
        if (bereinigteNote is not null && bereinigteNote.Length > 64)
        {
            return (false, "La note finale est trop longue (max. 64 caractères).");
        }

        var entscheidung = await dbContext.KlassenAbschlussEntscheidungen
            .FirstOrDefaultAsync(x => x.DurchlaufId == durchlaufId && x.StudentId == studentId, cancellationToken);
        if (entscheidung is null)
        {
            entscheidung = new KlassenAbschlussEntscheidung { DurchlaufId = durchlaufId, StudentId = studentId };
            await dbContext.KlassenAbschlussEntscheidungen.AddAsync(entscheidung, cancellationToken);
        }

        entscheidung.AbschlussNoteText = bereinigteNote;
        entscheidung.EntscheidetVonUserId ??= erfasstVonUserId;
        entscheidung.GeaendertAmUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, "La note finale a été enregistrée.");
    }
}
