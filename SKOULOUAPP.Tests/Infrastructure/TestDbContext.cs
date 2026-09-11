using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Finanzen;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
using HalloApp.Core.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HalloApp.Tests.Infrastructure;

/// <summary>
/// In-Memory-Datenbankkontext für Tests. Implementiert <see cref="IHalloAppDbContext"/>,
/// damit die Core-Services ohne die WebApp-Implementierung getestet werden können.
/// </summary>
public class TestDbContext(DbContextOptions<TestDbContext> options)
    : IdentityDbContext<AppUser, Role, Guid, IdentityUserClaim<Guid>, AppUserRole, IdentityUserLogin<Guid>, IdentityRoleClaim<Guid>, IdentityUserToken<Guid>>(options), IHalloAppDbContext
{
    public static readonly Guid DefaultKlasseId = Guid.Parse("10000000-0000-0000-0000-000000000001");
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
    public DbSet<FinanzKategorie> FinanzKategorien => Set<FinanzKategorie>();
    public DbSet<FinanzBuchung> FinanzBuchungen => Set<FinanzBuchung>();
    public DbSet<KursNiveauDurchlauf> KursNiveauDurchlaeufe => Set<KursNiveauDurchlauf>();
    public DbSet<RatenZahlung> RatenZahlungen => Set<RatenZahlung>();
    public DbSet<KlassenAbschlussEntscheidung> KlassenAbschlussEntscheidungen => Set<KlassenAbschlussEntscheidung>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
            .HasOne(x => x.Lehrer)
            .WithMany()
            .HasForeignKey(x => x.LehrerID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LehrerNiveauZuordnung>()
            .HasOne(x => x.Schule)
            .WithMany()
            .HasForeignKey(x => x.SchulID)
            .OnDelete(DeleteBehavior.Restrict);

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

        modelBuilder.Entity<BlobMediaAsset>()
            .HasOne(x => x.Owner)
            .WithMany()
            .HasForeignKey(x => x.OwnerId)
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

        // --- Finanzverwaltung: FK-Navigationen explizit auf die deutschen
        // ID-Eigenschaften mappen (sonst legt EF Shadow-FKs an und Include-Joins
        // filtern alle Zeilen weg). ---
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
    }

    /// <summary>Erstellt einen neuen In-Memory-Kontext mit einer eindeutigen Datenbank.</summary>
    public static TestDbContext CreateInMemory()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new TestDbContext(options);
        context.Klassen.Add(new Klasse { Id = DefaultKlasseId, Code = "TEST", Title = "Testklasse" });
        context.SaveChanges();
        return context;
    }
}
