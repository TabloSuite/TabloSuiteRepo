using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Models.Admin;
using SkoulouApp.Core.Models.TeacherLessons;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>
/// Erfassung durch die Direktion (« Saisi par la direction »): Schulleitung und
/// Koordination dürfen Lektionen, Lektionsübungen und Prüfungen im Namen eines
/// Lehrers der eigenen Schule erfassen – nie für fremde Schulen, nie gesperrt und
/// nie als einfacher Lehrer für einen Kollegen.
/// </summary>
public class DirektionsErfassungServiceTests
{
    private sealed class Szenario
    {
        public required TestDbContext Db { get; init; }
        public required School Schule { get; init; }
        public required School AndereSchule { get; init; }
        public required Klasse Klasse { get; init; }
        public required Klasse NichtZugeordneteKlasse { get; init; }
        public required AppUser Leiter { get; init; }
        public required AppUser Koordinator { get; init; }
        public required AppUser Lehrer { get; init; }
        public required AppUser Kollege { get; init; }
        public required AppUser FremderLehrer { get; init; }
        public required AppUser FremderLeiter { get; init; }
    }

    private static async Task<Szenario> ErstelleSzenarioAsync()
    {
        var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "École du Plateau", IsActive = true };
        var andereSchule = new School { Id = Guid.NewGuid(), Name = "École de la Lagune", IsActive = true };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "CM1", Title = "CM1" };
        var nichtZugeordnet = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "CM2", Title = "CM2" };
        var fremdeKlasse = new Klasse { Id = Guid.NewGuid(), SchoolId = andereSchule.Id, Code = "CM1", Title = "CM1" };

        var leiterRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter, NormalizedName = Rollen.SchulLeiter.ToUpperInvariant() };
        var koordinatorRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.Koordinator, NormalizedName = Rollen.Koordinator.ToUpperInvariant() };
        var lehrerRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.Lehrer, NormalizedName = Rollen.Lehrer.ToUpperInvariant() };

        var leiter = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", FullName = "Directrice Ama" };
        var koordinator = new AppUser { Id = Guid.NewGuid(), UserName = "koordinator", FullName = "Coordinateur Kofi" };
        var lehrer = new AppUser { Id = Guid.NewGuid(), UserName = "lehrer", FullName = "Mensah Yao" };
        var kollege = new AppUser { Id = Guid.NewGuid(), UserName = "kollege", FullName = "Adjo Afi" };
        var fremderLehrer = new AppUser { Id = Guid.NewGuid(), UserName = "fremder-lehrer", FullName = "Komla Edem" };
        var fremderLeiter = new AppUser { Id = Guid.NewGuid(), UserName = "fremder-leiter", FullName = "Directeur Koffi" };

        db.Schools.AddRange(schule, andereSchule);
        db.Klassen.AddRange(klasse, nichtZugeordnet, fremdeKlasse);
        db.Roles.AddRange(leiterRolle, koordinatorRolle, lehrerRolle);
        db.Users.AddRange(leiter, koordinator, lehrer, kollege, fremderLehrer, fremderLeiter);
        db.UserRoles.AddRange(
            new AppUserRole { UserId = leiter.Id, RoleId = leiterRolle.Id },
            new AppUserRole { UserId = koordinator.Id, RoleId = koordinatorRolle.Id },
            new AppUserRole { UserId = lehrer.Id, RoleId = lehrerRolle.Id },
            new AppUserRole { UserId = kollege.Id, RoleId = lehrerRolle.Id },
            new AppUserRole { UserId = fremderLehrer.Id, RoleId = lehrerRolle.Id },
            new AppUserRole { UserId = fremderLeiter.Id, RoleId = leiterRolle.Id });
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = leiter.Id, SchoolId = schule.Id },
            new UserSchool { AppUserId = koordinator.Id, SchoolId = schule.Id },
            new UserSchool { AppUserId = lehrer.Id, SchoolId = schule.Id },
            new UserSchool { AppUserId = kollege.Id, SchoolId = schule.Id },
            new UserSchool { AppUserId = fremderLehrer.Id, SchoolId = andereSchule.Id },
            new UserSchool { AppUserId = fremderLeiter.Id, SchoolId = andereSchule.Id });
        db.LehrerNiveauZuordnungen.AddRange(
            new LehrerNiveauZuordnung { Id = Guid.NewGuid(), LehrerID = lehrer.Id, SchulID = schule.Id, KlasseId = klasse.Id },
            new LehrerNiveauZuordnung { Id = Guid.NewGuid(), LehrerID = fremderLehrer.Id, SchulID = andereSchule.Id, KlasseId = fremdeKlasse.Id });
        await db.SaveChangesAsync();

        return new Szenario
        {
            Db = db,
            Schule = schule,
            AndereSchule = andereSchule,
            Klasse = klasse,
            NichtZugeordneteKlasse = nichtZugeordnet,
            Leiter = leiter,
            Koordinator = koordinator,
            Lehrer = lehrer,
            Kollege = kollege,
            FremderLehrer = fremderLehrer,
            FremderLeiter = fremderLeiter
        };
    }

    private static TeacherLessonFormModel SkriptFormular(Guid klasseId, string titel = "Les fractions") => new()
    {
        KlasseId = klasseId,
        Title = titel,
        IsPublished = true,
        Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>Une fraction représente une partie d'un tout.</p>" }]
    };

    private static ExamFormModel PruefungsFormular(Guid klasseId, bool fuerSchueler = true) => new()
    {
        Title = "Évaluation fractions",
        NiveauCode = "CM1",
        KlasseId = klasseId,
        AssignToClassStudents = fuerSchueler,
        ReleaseTarget = fuerSchueler ? SkoulouApp.Core.DataModels.Exams.ExamReleaseTarget.ClassStudents : SkoulouApp.Core.DataModels.Exams.ExamReleaseTarget.PrivateAuthor,
        Exercises =
        [
            new ExamExerciseFormModel
            {
                Questions =
                [
                    new ExamQuestionFormModel
                    {
                        Prompt = "1/2 + 1/2 = ?",
                        Options = [new() { Text = "1", IsCorrect = true }, new() { Text = "2" }]
                    }
                ]
            }
        ]
    };

    // ------------------------------------------------------------------
    // Berechtigungsprüfung
    // ------------------------------------------------------------------

    [Fact]
    public async Task DarfFuerLehrerHandeln_SchulleitungUndKoordinationDerselbenSchule_Erlaubt()
    {
        var s = await ErstelleSzenarioAsync();
        using var db = s.Db;
        var service = new DirektionsErfassungService(db);

        Assert.True(await service.DarfFuerLehrerHandelnAsync(s.Leiter.Id, s.Lehrer.Id));
        Assert.True(await service.DarfFuerLehrerHandelnAsync(s.Koordinator.Id, s.Lehrer.Id));
    }

    [Fact]
    public async Task DarfFuerLehrerHandeln_GleicherNutzer_Erlaubt()
    {
        var s = await ErstelleSzenarioAsync();
        using var db = s.Db;

        Assert.True(await new DirektionsErfassungService(db).DarfFuerLehrerHandelnAsync(s.Lehrer.Id, s.Lehrer.Id));
    }

    [Fact]
    public async Task DarfFuerLehrerHandeln_DirektionAndererSchule_Verweigert()
    {
        var s = await ErstelleSzenarioAsync();
        using var db = s.Db;

        Assert.False(await new DirektionsErfassungService(db).DarfFuerLehrerHandelnAsync(s.FremderLeiter.Id, s.Lehrer.Id));
    }

    [Fact]
    public async Task DarfFuerLehrerHandeln_LehrerAndererSchule_Verweigert()
    {
        var s = await ErstelleSzenarioAsync();
        using var db = s.Db;

        Assert.False(await new DirektionsErfassungService(db).DarfFuerLehrerHandelnAsync(s.Leiter.Id, s.FremderLehrer.Id));
    }

    [Fact]
    public async Task DarfFuerLehrerHandeln_GesperrteDirektionOderGesperrterLehrer_Verweigert()
    {
        var s = await ErstelleSzenarioAsync();
        using var db = s.Db;
        var service = new DirektionsErfassungService(db);

        s.Koordinator.IsBlocked = true;
        s.Kollege.IsBlocked = true;
        await db.SaveChangesAsync();

        Assert.False(await service.DarfFuerLehrerHandelnAsync(s.Koordinator.Id, s.Lehrer.Id));
        Assert.False(await service.DarfFuerLehrerHandelnAsync(s.Leiter.Id, s.Kollege.Id));
        Assert.True(await service.DarfFuerLehrerHandelnAsync(s.Leiter.Id, s.Lehrer.Id));
    }

    [Fact]
    public async Task DarfFuerLehrerHandeln_GesperrteSchule_Verweigert()
    {
        var s = await ErstelleSzenarioAsync();
        using var db = s.Db;
        s.Schule.IsActive = false;
        await db.SaveChangesAsync();

        Assert.False(await new DirektionsErfassungService(db).DarfFuerLehrerHandelnAsync(s.Leiter.Id, s.Lehrer.Id));
    }

    [Fact]
    public async Task DarfFuerLehrerHandeln_LehrerFuerKollegen_Verweigert()
    {
        var s = await ErstelleSzenarioAsync();
        using var db = s.Db;

        Assert.False(await new DirektionsErfassungService(db).DarfFuerLehrerHandelnAsync(s.Lehrer.Id, s.Kollege.Id));
    }

    [Fact]
    public async Task DarfFuerLehrerHandeln_ZielOhneLehrerrolle_Verweigert()
    {
        var s = await ErstelleSzenarioAsync();
        using var db = s.Db;

        Assert.False(await new DirektionsErfassungService(db).DarfFuerLehrerHandelnAsync(s.Leiter.Id, s.Koordinator.Id));
    }

    [Fact]
    public async Task GetLehrerFuerDirektion_LiefertNurAktiveLehrerDerEigenenSchule()
    {
        var s = await ErstelleSzenarioAsync();
        using var db = s.Db;
        s.Kollege.IsBlocked = true;
        await db.SaveChangesAsync();
        var service = new DirektionsErfassungService(db);

        var lehrer = await service.GetLehrerFuerDirektionAsync(s.Leiter.Id);

        var einziger = Assert.Single(lehrer);
        Assert.Equal(s.Lehrer.Id, einziger.Id);
        Assert.Equal("Mensah Yao", einziger.Name);
        Assert.Empty(await service.GetLehrerFuerDirektionAsync(s.Lehrer.Id));
    }

    // ------------------------------------------------------------------
    // Speichern im Namen des Lehrers
    // ------------------------------------------------------------------

    [Fact]
    public async Task SaveLessonAsync_ImNamenDesLehrers_SetztAutorUndErfasser_LehrerBearbeitungBehaeltKennzeichnung()
    {
        var s = await ErstelleSzenarioAsync();
        using var db = s.Db;
        var service = new TeacherLessonService(db);

        var id = await service.SaveLessonAsync(SkriptFormular(s.Klasse.Id), s.Lehrer.Id, handelnderId: s.Leiter.Id);

        var gespeichert = await db.TeacherLessons.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(s.Lehrer.Id, gespeichert.AuthorId);
        Assert.Equal(s.Leiter.Id, gespeichert.ErfasstVonId);

        var liste = await service.GetLessonsForTeacherAsync(s.Lehrer.Id);
        Assert.True(Assert.Single(liste).IstVonDirektionErfasst);

        // Der Lehrer bearbeitet später selbst: Kennzeichnung bleibt erhalten.
        var formular = await service.GetLessonForEditAsync(id, s.Lehrer.Id);
        Assert.NotNull(formular);
        Assert.True(formular!.IstVonDirektionErfasst);
        formular.Title = "Les fractions (révisé)";
        await service.SaveLessonAsync(formular, s.Lehrer.Id, handelnderId: s.Lehrer.Id);

        var nachBearbeitung = await db.TeacherLessons.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal("Les fractions (révisé)", nachBearbeitung.Title);
        Assert.Equal(s.Leiter.Id, nachBearbeitung.ErfasstVonId);
    }

    [Fact]
    public async Task SaveLessonAsync_DirektionAndererSchule_WirdAbgewiesen()
    {
        var s = await ErstelleSzenarioAsync();
        using var db = s.Db;
        var service = new TeacherLessonService(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SaveLessonAsync(SkriptFormular(s.Klasse.Id), s.Lehrer.Id, handelnderId: s.FremderLeiter.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SaveLessonAsync(SkriptFormular(s.Klasse.Id), s.Kollege.Id, handelnderId: s.Lehrer.Id));

        Assert.Empty(db.TeacherLessons);
    }

    [Fact]
    public async Task SaveLessonAsync_ImNamenDesLehrers_NurInSeinenKlassen()
    {
        var s = await ErstelleSzenarioAsync();
        using var db = s.Db;
        var service = new TeacherLessonService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveLessonAsync(SkriptFormular(s.NichtZugeordneteKlasse.Id), s.Lehrer.Id, handelnderId: s.Leiter.Id));
        Assert.Empty(db.TeacherLessons);
    }

    [Fact]
    public async Task VeroeffentlichenUndLoeschen_DirektionNurMitBerechtigung()
    {
        var s = await ErstelleSzenarioAsync();
        using var db = s.Db;
        var service = new TeacherLessonService(db);
        var formular = SkriptFormular(s.Klasse.Id);
        formular.IsPublished = false;
        var id = await service.SaveLessonAsync(formular, s.Lehrer.Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SetPublishedAsync(id, s.Lehrer.Id, true, handelnderId: s.FremderLeiter.Id));
        await service.SetPublishedAsync(id, s.Lehrer.Id, true, handelnderId: s.Koordinator.Id);
        Assert.True((await db.TeacherLessons.AsNoTracking().SingleAsync(x => x.Id == id)).IsPublished);

        Assert.Null(await service.GetLessonForEditAsync(id, s.Lehrer.Id, handelnderId: s.FremderLeiter.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.DeleteLessonAsync(id, s.Lehrer.Id, handelnderId: s.FremderLeiter.Id));
        Assert.Single(db.TeacherLessons);

        await service.DeleteLessonAsync(id, s.Lehrer.Id, handelnderId: s.Leiter.Id);
        Assert.Empty(db.TeacherLessons);
    }

    [Fact]
    public async Task SaveExamAsync_ImNamenDesLehrers_VerhaeltSichWieEigenePruefungUndIstGekennzeichnet()
    {
        var s = await ErstelleSzenarioAsync();
        using var db = s.Db;
        var service = new ExamService(db);

        var id = await service.SaveExamAsync(s.Lehrer.Id, PruefungsFormular(s.Klasse.Id), handelnderId: s.Leiter.Id);

        var exam = await db.Exams.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(s.Lehrer.Id, exam.AuthorId);
        Assert.Equal(s.Leiter.Id, exam.ErfasstVonId);
        // Wie eine eigene Prüfung des Lehrers: für die Schüler veröffentlicht, nicht als reine Direktions-PDF.
        Assert.True(exam.IsPublishedToStudents);
        Assert.False(exam.IsSharedWithSchool);

        var liste = await service.GetTeacherExamsAsync(s.Lehrer.Id);
        Assert.True(Assert.Single(liste).IstVonDirektionErfasst);

        // Vorschau/PDF für die erfassende Direktion, nicht für eine fremde Schule.
        Assert.NotNull(await service.GetExamForPdfAsync(s.Leiter.Id, id));
        Assert.Null(await service.GetExamForPdfAsync(s.FremderLeiter.Id, id));
    }

    [Fact]
    public async Task SaveExamAsync_DirektionAndererSchule_WirdAbgewiesen()
    {
        var s = await ErstelleSzenarioAsync();
        using var db = s.Db;
        var service = new ExamService(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SaveExamAsync(s.Lehrer.Id, PruefungsFormular(s.Klasse.Id), handelnderId: s.FremderLeiter.Id));
        Assert.Empty(db.Exams);

        var id = await service.SaveExamAsync(s.Lehrer.Id, PruefungsFormular(s.Klasse.Id, fuerSchueler: false));
        Assert.Null((await db.Exams.AsNoTracking().SingleAsync(x => x.Id == id)).ErfasstVonId);
        Assert.Null(await service.GetExamForEditAsync(s.Lehrer.Id, id, handelnderId: s.FremderLeiter.Id));
        Assert.False((await service.DeleteExamAsync(s.Lehrer.Id, id, handelnderId: s.FremderLeiter.Id)).Succeeded);
        Assert.True((await service.DeleteExamAsync(s.Lehrer.Id, id, handelnderId: s.Koordinator.Id)).Succeeded);
        Assert.Empty(db.Exams);
    }

    [Fact]
    public async Task SaveLessonExercisesAsync_ImNamenDesLehrers_SetztErstellerUndErfasser()
    {
        var s = await ErstelleSzenarioAsync();
        using var db = s.Db;
        var service = new DashboardService(db, TestUserManager.Create([]), new FakeMediaStorageService());
        var formular = new LessonFormModel
        {
            CourseId = s.Klasse.Id,
            Title = "Lecture : le marché",
            Summary = "Texte et questions",
            Content = "<p>Au marché de Lomé…</p>",
            IsPublished = true
        };

        var abgewiesen = await service.SaveLessonAsync(formular, s.Lehrer.Id, handelnderId: s.FremderLeiter.Id);
        Assert.False(abgewiesen.Succeeded);
        Assert.Empty(db.Lessons);

        var ergebnis = await service.SaveLessonAsync(formular, s.Lehrer.Id, handelnderId: s.Leiter.Id);

        Assert.True(ergebnis.Succeeded, ergebnis.Message);
        var lesson = await db.Lessons.AsNoTracking().SingleAsync();
        Assert.Equal(s.Lehrer.Id, lesson.CreatedByTeacherId);
        Assert.Equal(s.Leiter.Id, lesson.ErfasstVonId);

        // Der Lehrer bearbeitet selbst: Kennzeichnung bleibt erhalten.
        var bearbeitung = await service.GetLessonForEditAsync(lesson.Id);
        Assert.NotNull(bearbeitung);
        Assert.True(bearbeitung!.IstVonDirektionErfasst);
        bearbeitung.Title = "Lecture : le marché (v2)";
        Assert.True((await service.SaveLessonAsync(bearbeitung, s.Lehrer.Id, handelnderId: s.Lehrer.Id)).Succeeded);
        Assert.Equal(s.Leiter.Id, (await db.Lessons.AsNoTracking().SingleAsync()).ErfasstVonId);
    }
}
