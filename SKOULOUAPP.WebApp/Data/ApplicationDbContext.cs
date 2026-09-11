using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using HalloApp.Core.Interfaces;

namespace HalloApp.WebApp.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<AppUser, Role, Guid, IdentityUserClaim<Guid>, AppUserRole, IdentityUserLogin<Guid>, IdentityRoleClaim<Guid>, IdentityUserToken<Guid>>(options), IHalloAppDbContext
{
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<School> Schools => Set<School>();
    public DbSet<Klasse> Klassen => Set<Klasse>();
    public DbSet<KlassenStufe> KlassenStufen => Set<KlassenStufe>();
    public DbSet<Fach> Faecher => Set<Fach>();
    public DbSet<LehrerFachZuordnung> LehrerFachZuordnungen => Set<LehrerFachZuordnung>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<LessonMedia> LessonMedia => Set<LessonMedia>();
    public DbSet<BlobMediaAsset> BlobMediaAssets => Set<BlobMediaAsset>();
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<ExerciseOption> ExerciseOptions => Set<ExerciseOption>();
    public DbSet<ExerciseAttempt> ExerciseAttempts => Set<ExerciseAttempt>();
    public DbSet<LessonProgress> LessonProgresses => Set<LessonProgress>();
    public DbSet<TeacherLesson> TeacherLessons => Set<TeacherLesson>();
    public DbSet<TeacherLessonPage> TeacherLessonPages => Set<TeacherLessonPage>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamExercise> ExamExercises => Set<ExamExercise>();
    public DbSet<ExamQuestion> ExamQuestions => Set<ExamQuestion>();
    public DbSet<ExamQuestionOption> ExamQuestionOptions => Set<ExamQuestionOption>();
    public DbSet<ExamAttempt> ExamAttempts => Set<ExamAttempt>();
    public DbSet<ExamAnswer> ExamAnswers => Set<ExamAnswer>();
    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();
    public DbSet<UserSchool> UserSchools => Set<UserSchool>();
    public DbSet<LehrerNiveauZuordnung> LehrerNiveauZuordnungen => Set<LehrerNiveauZuordnung>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<LevelEnrollment> LevelEnrollments => Set<LevelEnrollment>();
    public DbSet<Abrechnungsperiode> Abrechnungsperioden => Set<Abrechnungsperiode>();
    public DbSet<LehrerProvisionsAuszahlung> LehrerProvisionsAuszahlungen => Set<LehrerProvisionsAuszahlung>();
    public DbSet<ProduktOwnerProvisionsAuszahlung> ProduktOwnerProvisionsAuszahlungen => Set<ProduktOwnerProvisionsAuszahlung>();
    public DbSet<CommissionSettings> CommissionSettings => Set<CommissionSettings>();
    public DbSet<ExceptionLog> ExceptionLogs => Set<ExceptionLog>();

    public DbSet<FinanzKategorie> FinanzKategorien => Set<FinanzKategorie>();
    public DbSet<FinanzBuchung> FinanzBuchungen => Set<FinanzBuchung>();
    public DbSet<KursNiveauDurchlauf> KursNiveauDurchlaeufe => Set<KursNiveauDurchlauf>();
    public DbSet<RatenZahlung> RatenZahlungen => Set<RatenZahlung>();
    public DbSet<KlassenAbschlussEntscheidung> KlassenAbschlussEntscheidungen => Set<KlassenAbschlussEntscheidung>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<School>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<School>().Property(x => x.SchoolLeaderCommissionPercentOverride).HasPrecision(5, 2);
        modelBuilder.Entity<School>().Property(x => x.TeacherCommissionPercentOverride).HasPrecision(5, 2);
        modelBuilder.Entity<School>().Property(x => x.ProductOwnerCommissionPercentOverride).HasPrecision(5, 2);
        modelBuilder.Entity<Role>().Property(x => x.Name).HasMaxLength(100);
        modelBuilder.Entity<Permission>().Property(x => x.Name).HasMaxLength(150);
        modelBuilder.Entity<AppUser>().Property(x => x.Email).HasMaxLength(256);
        modelBuilder.Entity<AppUser>().Property(x => x.FullName).HasMaxLength(200);
        modelBuilder.Entity<AppUser>().Property(x => x.Vorname).HasMaxLength(100);
        modelBuilder.Entity<AppUser>().Property(x => x.Nachname).HasMaxLength(100);
        modelBuilder.Entity<AppUser>().Property(x => x.TelefonNummer).HasMaxLength(50);
        modelBuilder.Entity<AppUser>().Property(x => x.AgbVersion).HasMaxLength(32);
        modelBuilder.Entity<Fach>().Property(x => x.Name).HasMaxLength(120);
        modelBuilder.Entity<StudentProfile>().Property(x => x.ElternTelefonNummer).HasMaxLength(50);
        modelBuilder.Entity<StudentProfile>().Property(x => x.ElternEmail).HasMaxLength(256);
        modelBuilder.Entity<StudentProfile>().Property(x => x.IsArchived).HasDefaultValue(false);
        modelBuilder.Entity<Klasse>().Property(x => x.Code).HasMaxLength(20);
        modelBuilder.Entity<KlassenStufe>().Property(x => x.Name).HasMaxLength(100);

        modelBuilder.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<School>().HasIndex(x => x.Name).IsUnique();
        // Klassen-Code ist pro Schule eindeutig. Globale System-Niveaus (SchoolId == null)
        // teilen sich denselben Namensraum; schul-eigene Klassen dürfen denselben Code
        // in verschiedenen Schulen tragen.
        modelBuilder.Entity<Klasse>().HasIndex(x => new { x.SchoolId, x.Code }).IsUnique();
        modelBuilder.Entity<KlassenStufe>().HasIndex(x => new { x.SchoolId, x.Name }).IsUnique();
        modelBuilder.Entity<Fach>().HasIndex(x => new { x.SchoolId, x.Name }).IsUnique();
        modelBuilder.Entity<LehrerFachZuordnung>().HasIndex(x => new { x.LehrerId, x.FachId }).IsUnique();
        // Restrict statt Cascade: Andernfalls entstehen mehrere Kaskadenpfade zur
        // LevelEnrollments-Tabelle (School -> Klasse -> LevelEnrollment und
        // School -> StudentProfile -> LevelEnrollment), was SQL Server ablehnt.
        // Klassen werden ohnehin über die Schulleiter-Governance verwaltet.
        modelBuilder.Entity<Klasse>()
            .HasOne(x => x.School)
            .WithMany()
            .HasForeignKey(x => x.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<KlassenStufe>()
            .HasOne(x => x.School)
            .WithMany(x => x.KlassenStufen)
            .HasForeignKey(x => x.SchoolId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Fach>()
            .HasOne(x => x.School)
            .WithMany()
            .HasForeignKey(x => x.SchoolId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<LehrerFachZuordnung>()
            .HasOne(x => x.Lehrer)
            .WithMany(x => x.LehrerFachZuordnungen)
            .HasForeignKey(x => x.LehrerId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<LehrerFachZuordnung>()
            .HasOne(x => x.Fach)
            .WithMany(x => x.LehrerZuordnungen)
            .HasForeignKey(x => x.FachId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AppUser>()
            .HasOne(x => x.ProfilBildAsset)
            .WithMany()
            .HasForeignKey(x => x.ProfilBildAssetId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<BlobMediaAsset>().Property(x => x.BlobName).HasMaxLength(600);
        modelBuilder.Entity<BlobMediaAsset>().Property(x => x.OriginalFileName).HasMaxLength(260);
        modelBuilder.Entity<BlobMediaAsset>().Property(x => x.DisplayName).HasMaxLength(200);
        modelBuilder.Entity<BlobMediaAsset>().Property(x => x.ContentType).HasMaxLength(100);
        modelBuilder.Entity<BlobMediaAsset>().Property(x => x.AltText).HasMaxLength(250);
        modelBuilder.Entity<Exercise>().Property(x => x.TtsLanguage).HasMaxLength(10).HasDefaultValue("de-DE");
        modelBuilder.Entity<ExamQuestion>().Property(x => x.TtsLanguage).HasMaxLength(10).HasDefaultValue("de-DE");
        modelBuilder.Entity<BlobMediaAsset>().HasIndex(x => new { x.SchoolId, x.ErstelltAmUtc });
        modelBuilder.Entity<BlobMediaAsset>().HasIndex(x => new { x.SchoolId, x.KlasseId });
        modelBuilder.Entity<BlobMediaAsset>()
            .HasOne(x => x.School)
            .WithMany()
            .HasForeignKey(x => x.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BlobMediaAsset>()
            .HasOne(x => x.Owner)
            .WithMany()
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BlobMediaAsset>()
            .HasOne(x => x.Klasse)
            .WithMany()
            .HasForeignKey(x => x.KlasseId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Klasse>()
            .HasOne(x => x.KlassenStufe)
            .WithMany(x => x.Klassen)
            .HasForeignKey(x => x.KlassenStufeId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Role>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<Permission>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<ExceptionLog>().HasIndex(x => x.OccurredAtUtc);
        modelBuilder.Entity<ExceptionLog>().HasIndex(x => new { x.SchoolId, x.OccurredAtUtc });

        modelBuilder.Entity<ExceptionLog>().Property(x => x.Message).HasMaxLength(4000);
        modelBuilder.Entity<ExceptionLog>().Property(x => x.ExceptionType).HasMaxLength(500);
        modelBuilder.Entity<ExceptionLog>().Property(x => x.RequestPath).HasMaxLength(2000);

        modelBuilder.Entity<ExceptionLog>()
            .HasOne(x => x.School)
            .WithMany()
            .HasForeignKey(x => x.SchoolId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<AppUserRole>().HasKey(x => new { x.UserId, x.RoleId });
        modelBuilder.Entity<UserSchool>().HasKey(x => new { x.AppUserId, x.SchoolId });
        modelBuilder.Entity<RolePermission>().HasKey(x => new { x.RoleId, x.PermissionId });

        modelBuilder.Entity<AppUserRole>()
            .HasOne(x => x.AppUser)
            .WithMany(x => x.UserRoles)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AppUserRole>()
            .HasOne(x => x.Role)
            .WithMany(x => x.UserRoles)
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserSchool>()
            .HasOne(x => x.AppUser)
            .WithMany(x => x.UserSchools)
            .HasForeignKey(x => x.AppUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserSchool>()
            .HasOne(x => x.School)
            .WithMany(x => x.UserSchools)
            .HasForeignKey(x => x.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LehrerNiveauZuordnung>()
            .HasIndex(x => new { x.LehrerID, x.SchulID, x.KlasseId }).IsUnique();

        modelBuilder.Entity<LehrerNiveauZuordnung>()
            .HasOne(x => x.Lehrer)
            .WithMany()
            .HasForeignKey(x => x.LehrerID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LehrerNiveauZuordnung>()
            .HasOne(x => x.Schule)
            .WithMany()
            .HasForeignKey(x => x.SchulID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LehrerNiveauZuordnung>()
            .HasOne(x => x.Klasse)
            .WithMany()
            .HasForeignKey(x => x.KlasseId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<StudentProfile>()
            .HasOne(x => x.CurrentKlasse)
            .WithMany()
            .HasForeignKey(x => x.CurrentKlasseId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<RolePermission>()
            .HasOne(x => x.Role)
            .WithMany(x => x.RolePermissions)
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<RolePermission>()
            .HasOne(x => x.Permission)
            .WithMany(x => x.RolePermissions)
            .HasForeignKey(x => x.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<StudentProfile>()
            .HasOne(x => x.AppUser)
            .WithMany(x => x.StudentProfiles)
            .HasForeignKey(x => x.AppUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<StudentProfile>()
            .HasOne(x => x.School)
            .WithMany(x => x.Students)
            .HasForeignKey(x => x.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Exercise>().Property(x => x.Title).HasMaxLength(200);
        modelBuilder.Entity<Exercise>().Property(x => x.Question).HasMaxLength(1000);
        modelBuilder.Entity<Exercise>().Property(x => x.Points).HasDefaultValue(10);
        modelBuilder.Entity<Lesson>().Property(x => x.IsPublished).HasDefaultValue(true);
        modelBuilder.Entity<Lesson>().Property(x => x.AssignmentVersion).HasDefaultValue(1);
        modelBuilder.Entity<ExerciseOption>().Property(x => x.Text).HasMaxLength(500);
        modelBuilder.Entity<ExerciseAttempt>().Property(x => x.ManualFeedback).HasMaxLength(2000);
        modelBuilder.Entity<ExerciseAttempt>().Property(x => x.AwardedPoints).HasDefaultValue(0);
        modelBuilder.Entity<ExerciseAttempt>().Property(x => x.AssignmentVersion).HasDefaultValue(1);
        modelBuilder.Entity<ExerciseAttempt>().Property(x => x.ExerciseSnapshotJson).HasMaxLength(4000);

        modelBuilder.Entity<ExerciseOption>()
            .HasOne(x => x.Exercise)
            .WithMany(x => x.Options)
            .HasForeignKey(x => x.ExerciseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExerciseAttempt>()
            .HasOne(x => x.Exercise)
            .WithMany(x => x.Attempts)
            .HasForeignKey(x => x.ExerciseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExerciseAttempt>()
            .HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ExerciseAttempt>()
            .HasIndex(x => new { x.ExerciseId, x.StudentId });

        modelBuilder.Entity<ExerciseAttempt>()
            .HasIndex(x => new { x.ExerciseId, x.StudentId, x.AssignmentVersion });

        modelBuilder.Entity<LessonProgress>()
            .HasOne(x => x.Lesson)
            .WithMany()
            .HasForeignKey(x => x.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LessonProgress>()
            .HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LessonProgress>()
            .HasIndex(x => new { x.LessonId, x.StudentId })
            .IsUnique();

        modelBuilder.Entity<Exam>().Property(x => x.Title).HasMaxLength(200);
        modelBuilder.Entity<Exam>().Property(x => x.ExamType).HasConversion<int>();
        modelBuilder.Entity<ExamQuestion>().Property(x => x.Prompt).HasMaxLength(1000);
        modelBuilder.Entity<ExamQuestionOption>().Property(x => x.Text).HasMaxLength(500);

        modelBuilder.Entity<ExamQuestion>()
            .HasOne(x => x.Exam)
            .WithMany(x => x.Questions)
            .HasForeignKey(x => x.ExamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExamExercise>().Property(x => x.Title).HasMaxLength(200);
        modelBuilder.Entity<ExamExercise>().Property(x => x.Consigne).HasMaxLength(1000);

        modelBuilder.Entity<ExamExercise>()
            .HasOne(x => x.Exam)
            .WithMany(x => x.Exercises)
            .HasForeignKey(x => x.ExamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExamQuestion>()
            .HasOne(x => x.ExamExercise)
            .WithMany(x => x.Questions)
            .HasForeignKey(x => x.ExamExerciseId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExamQuestionOption>()
            .HasOne(x => x.ExamQuestion)
            .WithMany(x => x.Options)
            .HasForeignKey(x => x.ExamQuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExamAttempt>()
            .HasOne(x => x.Exam)
            .WithMany(x => x.Attempts)
            .HasForeignKey(x => x.ExamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExamAttempt>()
            .HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ExamAttempt>()
            .HasIndex(x => new { x.ExamId, x.StudentId });
        modelBuilder.Entity<ExamAttempt>().Property(x => x.ManuelleNote).HasPrecision(5, 2);

        modelBuilder.Entity<ExamAnswer>()
            .Property(x => x.FreeTextAnswer)
            .HasMaxLength(4000);

        modelBuilder.Entity<ExamAnswer>()
            .HasOne(x => x.ExamAttempt)
            .WithMany(x => x.Answers)
            .HasForeignKey(x => x.ExamAttemptId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExamAnswer>()
            .HasOne(x => x.ExamQuestion)
            .WithMany()
            .HasForeignKey(x => x.ExamQuestionId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Klasse>().Property(x => x.Price).HasPrecision(12, 2);

        modelBuilder.Entity<LevelEnrollment>().Property(x => x.PriceAtEnrollment).HasPrecision(12, 2);
        modelBuilder.Entity<LevelEnrollment>().Property(x => x.SchoolLeaderCommissionAtEnrollment).HasPrecision(12, 2);
        modelBuilder.Entity<LevelEnrollment>().Property(x => x.TeacherCommissionAtEnrollment).HasPrecision(12, 2);
        modelBuilder.Entity<LevelEnrollment>().Property(x => x.ProductOwnerCommissionAtEnrollment).HasPrecision(12, 2);
        modelBuilder.Entity<CommissionSettings>().Property(x => x.SchoolLeaderPercent).HasPrecision(5, 2);
        modelBuilder.Entity<CommissionSettings>().Property(x => x.TeacherPercent).HasPrecision(5, 2);
        modelBuilder.Entity<CommissionSettings>().Property(x => x.ProductOwnerPercent).HasPrecision(5, 2);
        modelBuilder.Entity<CommissionSettings>().Property(x => x.GlobalSubscriptionPrice).HasPrecision(18, 2);
        modelBuilder.Entity<LehrerNiveauZuordnung>().Property(x => x.Wochenstunden).HasPrecision(5, 2);

        modelBuilder.Entity<LevelEnrollment>()
            .HasOne(x => x.Student)
            .WithMany(x => x.LevelEnrollments)
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LevelEnrollment>()
            .HasOne(x => x.Klasse)
            .WithMany(x => x.Enrollments)
            .HasForeignKey(x => x.KlasseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LevelEnrollment>()
            .HasIndex(x => new { x.StudentId, x.KlasseId })
            .IsUnique();

        modelBuilder.Entity<LevelEnrollment>()
            .Property(x => x.AboTyp)
            .HasConversion<int>();

        modelBuilder.Entity<LevelEnrollment>()
            .HasOne(x => x.AbgerechnetInPeriode)
            .WithMany(x => x.Freischaltungen)
            .HasForeignKey(x => x.AbgerechnetInPeriodeId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<School>().Property(x => x.PreisHalloBasis).HasPrecision(12, 2);
        modelBuilder.Entity<School>().Property(x => x.PreisHalloPlus).HasPrecision(12, 2);

        modelBuilder.Entity<Abrechnungsperiode>().Property(x => x.Umsatz).HasPrecision(12, 2);
        modelBuilder.Entity<LehrerProvisionsAuszahlung>().Property(x => x.WochenstundenSnapshot).HasPrecision(5, 2);
        modelBuilder.Entity<LehrerProvisionsAuszahlung>().Property(x => x.GesamtWochenstundenSnapshot).HasPrecision(5, 2);
        modelBuilder.Entity<LehrerProvisionsAuszahlung>().Property(x => x.Betrag).HasPrecision(12, 2);
        modelBuilder.Entity<ProduktOwnerProvisionsAuszahlung>().Property(x => x.Betrag).HasPrecision(12, 2);

        modelBuilder.Entity<Abrechnungsperiode>()
            .HasOne(x => x.Schule)
            .WithMany(x => x.Abrechnungsperioden)
            .HasForeignKey(x => x.SchulID)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Abrechnungsperiode>()
            .HasIndex(x => new { x.SchulID, x.VonUtc });

        modelBuilder.Entity<LehrerProvisionsAuszahlung>()
            .HasOne(x => x.Abrechnungsperiode)
            .WithMany()
            .HasForeignKey(x => x.AbrechnungsperiodeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LehrerProvisionsAuszahlung>()
            .HasOne(x => x.Freischaltung)
            .WithMany()
            .HasForeignKey(x => x.FreischaltungId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LehrerProvisionsAuszahlung>()
            .HasOne(x => x.Lehrer)
            .WithMany()
            .HasForeignKey(x => x.LehrerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LehrerProvisionsAuszahlung>()
            .HasIndex(x => new { x.AbrechnungsperiodeId, x.FreischaltungId, x.LehrerId })
            .IsUnique();

        modelBuilder.Entity<ProduktOwnerProvisionsAuszahlung>()
            .HasOne(x => x.Abrechnungsperiode)
            .WithMany()
            .HasForeignKey(x => x.AbrechnungsperiodeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProduktOwnerProvisionsAuszahlung>()
            .HasOne(x => x.Freischaltung)
            .WithMany()
            .HasForeignKey(x => x.FreischaltungId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProduktOwnerProvisionsAuszahlung>()
            .HasOne(x => x.ProduktOwner)
            .WithMany()
            .HasForeignKey(x => x.ProduktOwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProduktOwnerProvisionsAuszahlung>()
            .HasIndex(x => new { x.AbrechnungsperiodeId, x.FreischaltungId, x.ProduktOwnerId })
            .IsUnique();

        modelBuilder.Entity<TeacherLesson>()
            .Property(x => x.Title)
            .HasMaxLength(200);

        modelBuilder.Entity<TeacherLesson>()
            .Property(x => x.Summary)
            .HasMaxLength(1000);

        modelBuilder.Entity<TeacherLesson>()
            .Property(x => x.CourseTheme)
            .HasMaxLength(200);

        modelBuilder.Entity<TeacherLesson>()
            .HasOne(x => x.Klasse)
            .WithMany()
            .HasForeignKey(x => x.KlasseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TeacherLesson>()
            .HasOne(x => x.Author)
            .WithMany()
            .HasForeignKey(x => x.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TeacherLesson>()
            .HasOne(x => x.VideoAsset)
            .WithMany()
            .HasForeignKey(x => x.VideoAssetId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<TeacherLesson>()
            .HasOne(x => x.Fach)
            .WithMany(x => x.TeacherLessons)
            .HasForeignKey(x => x.FachId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<TeacherLesson>()
            .HasIndex(x => new { x.KlasseId, x.Order });

        modelBuilder.Entity<TeacherLessonPage>()
            .HasOne(x => x.TeacherLesson)
            .WithMany(x => x.Pages)
            .HasForeignKey(x => x.TeacherLessonId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TeacherLessonPage>()
            .HasIndex(x => new { x.TeacherLessonId, x.PageNumber })
            .IsUnique();

        ConfigureFinanzen(modelBuilder);
    }

    private static void ConfigureFinanzen(ModelBuilder modelBuilder)
    {
        // --- FinanzKategorie ---
        modelBuilder.Entity<FinanzKategorie>().Property(x => x.Bezeichnung).HasMaxLength(150);
        modelBuilder.Entity<FinanzKategorie>().HasIndex(x => new { x.SchulID, x.Richtung, x.Bezeichnung });

        modelBuilder.Entity<FinanzKategorie>()
            .HasOne(x => x.Schule)
            .WithMany()
            .HasForeignKey(x => x.SchulID)
            .OnDelete(DeleteBehavior.Cascade);

        // --- FinanzBuchung ---
        modelBuilder.Entity<FinanzBuchung>().Property(x => x.Betrag).HasPrecision(18, 2);
        modelBuilder.Entity<FinanzBuchung>().HasIndex(x => new { x.SchulID, x.Wirkungsdatum });
        modelBuilder.Entity<FinanzBuchung>().HasIndex(x => new { x.SchulID, x.Richtung, x.Wirkungsdatum });

        modelBuilder.Entity<FinanzBuchung>()
            .HasOne(x => x.Schule)
            .WithMany()
            .HasForeignKey(x => x.SchulID)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FinanzBuchung>()
            .HasOne(x => x.Kategorie)
            .WithMany(x => x.Buchungen)
            .HasForeignKey(x => x.KategorieID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FinanzBuchung>()
            .HasOne(x => x.ErfasstVon)
            .WithMany()
            .HasForeignKey(x => x.ErfasstVonUserID)
            .OnDelete(DeleteBehavior.Restrict);

        // --- KursNiveauDurchlauf ---
        modelBuilder.Entity<KursNiveauDurchlauf>().Property(x => x.NiveauPreis).HasPrecision(18, 2);
        modelBuilder.Entity<KursNiveauDurchlauf>().HasIndex(x => new { x.SchulID, x.KlasseId, x.EndDatum });

        modelBuilder.Entity<KursNiveauDurchlauf>()
            .HasOne(x => x.Schule)
            .WithMany()
            .HasForeignKey(x => x.SchulID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<KursNiveauDurchlauf>()
            .HasOne(x => x.Klasse)
            .WithMany()
            .HasForeignKey(x => x.KlasseId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<KursNiveauDurchlauf>()
            .HasOne(x => x.ErstelltVon)
            .WithMany()
            .HasForeignKey(x => x.ErstelltVonUserID)
            .OnDelete(DeleteBehavior.Restrict);

        // --- KlassenAbschlussEntscheidung ---
        modelBuilder.Entity<KlassenAbschlussEntscheidung>().Property(x => x.AbschlussNote).HasPrecision(5, 2);
        modelBuilder.Entity<KlassenAbschlussEntscheidung>().Property(x => x.AbschlussNoteText).HasMaxLength(64);
        modelBuilder.Entity<KlassenAbschlussEntscheidung>()
            .HasIndex(x => new { x.DurchlaufId, x.StudentId })
            .IsUnique();

        modelBuilder.Entity<KlassenAbschlussEntscheidung>()
            .HasOne(x => x.Durchlauf)
            .WithMany()
            .HasForeignKey(x => x.DurchlaufId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<KlassenAbschlussEntscheidung>()
            .HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<KlassenAbschlussEntscheidung>()
            .HasOne(x => x.EntscheidetVon)
            .WithMany()
            .HasForeignKey(x => x.EntscheidetVonUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- RatenZahlung ---
        modelBuilder.Entity<RatenZahlung>().Property(x => x.Betrag).HasPrecision(18, 2);
        modelBuilder.Entity<RatenZahlung>().HasIndex(x => new { x.DurchlaufID, x.StudentId });

        modelBuilder.Entity<RatenZahlung>()
            .HasOne(x => x.Durchlauf)
            .WithMany(x => x.RatenZahlungen)
            .HasForeignKey(x => x.DurchlaufID)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RatenZahlung>()
            .HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<RatenZahlung>()
            .HasOne(x => x.ErfasstVon)
            .WithMany()
            .HasForeignKey(x => x.ErfasstVonUserID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<RatenZahlung>()
            .HasOne(x => x.FinanzBuchung)
            .WithMany()
            .HasForeignKey(x => x.FinanzBuchungID)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

