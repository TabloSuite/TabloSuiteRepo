using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Finanzen;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace HalloApp.Core.Interfaces;

/// <summary>
/// Abstraktion des Datenbankkontexts, damit die Business-Services in
/// HalloApp.Core auf die EF-Core-Daten zugreifen können, ohne die konkrete
/// ApplicationDbContext-Implementierung (in HalloApp.WebApp) zu kennen.
/// </summary>
public interface IHalloAppDbContext
{
    /// <summary>Datenbankzugriff für explizite, transaktionale Wartungsabläufe.</summary>
    DatabaseFacade Database { get; }

    /// <summary>Benutzerkonten (Identity).</summary>
    DbSet<AppUser> Users { get; }

    /// <summary>Rollen (Identity).</summary>
    DbSet<Role> Roles { get; }

    /// <summary>Zuordnung von Benutzerkonten zu Rollen.</summary>
    DbSet<AppUserRole> UserRoles { get; }

    /// <summary>Berechtigungen.</summary>
    DbSet<Permission> Permissions { get; }

    /// <summary>Rollen-Berechtigungs-Zuordnungen.</summary>
    DbSet<RolePermission> RolePermissions { get; }

    /// <summary>Schulen.</summary>
    DbSet<School> Schools { get; }

    /// <summary>Zuordnung Benutzer zu Schulen.</summary>
    DbSet<UserSchool> UserSchools { get; }

    /// <summary>Zuordnung Lehrer zu Niveaus einer Schule.</summary>
    DbSet<LehrerNiveauZuordnung> LehrerNiveauZuordnungen { get; }

    /// <summary>Schülerprofile.</summary>
    DbSet<StudentProfile> StudentProfiles { get; }

    /// <summary>Kursniveaus.</summary>
    DbSet<Klasse> Klassen { get; }

    /// <summary>Schulgebundene Klassenstufen normaler Schulen.</summary>
    DbSet<KlassenStufe> KlassenStufen { get; }

    /// <summary>Einschreibungen in Kursniveaus.</summary>
    DbSet<LevelEnrollment> LevelEnrollments { get; }

    /// <summary>Abrechnungszeiträume je Schule.</summary>
    DbSet<Abrechnungsperiode> Abrechnungsperioden { get; }

    /// <summary>Eingefrorene Lehrer-Provisionsanteile je Abrechnungsperiode.</summary>
    DbSet<LehrerProvisionsAuszahlung> LehrerProvisionsAuszahlungen { get; }

    /// <summary>Eingefrorene ProduktOwner-Provisionsanteile je Abrechnungsperiode.</summary>
    DbSet<ProduktOwnerProvisionsAuszahlung> ProduktOwnerProvisionsAuszahlungen { get; }

    /// <summary>Zentrale Kommissionskonfiguration.</summary>
    DbSet<CommissionSettings> CommissionSettings { get; }

    /// <summary>Lektionen.</summary>
    DbSet<Lesson> Lessons { get; }

    /// <summary>Medien einer Lektion.</summary>
    DbSet<LessonMedia> LessonMedia { get; }

    /// <summary>Private Bild-Assets der Azure-Blob-Bibliothek.</summary>
    DbSet<BlobMediaAsset> BlobMediaAssets { get; }

    /// <summary>Lernfortschritt je Lektion.</summary>
    DbSet<LessonProgress> LessonProgresses { get; }

    /// <summary>Vom Lehrer erstellte Kursskripte (Vorlesungsskripte).</summary>
    DbSet<TeacherLesson> TeacherLessons { get; }

    /// <summary>Seiten eines Kursskripts.</summary>
    DbSet<TeacherLessonPage> TeacherLessonPages { get; }

    /// <summary>Übungen.</summary>
    DbSet<Exercise> Exercises { get; }

    /// <summary>Antwortoptionen einer Übung.</summary>
    DbSet<ExerciseOption> ExerciseOptions { get; }

    /// <summary>Übungsversuche.</summary>
    DbSet<ExerciseAttempt> ExerciseAttempts { get; }

    /// <summary>Prüfungen.</summary>
    DbSet<Exam> Exams { get; }

    /// <summary>Aufgabenblöcke (Exercices) einer Prüfung.</summary>
    DbSet<ExamExercise> ExamExercises { get; }

    /// <summary>Prüfungsfragen.</summary>
    DbSet<ExamQuestion> ExamQuestions { get; }

    /// <summary>Antwortoptionen einer Prüfungsfrage.</summary>
    DbSet<ExamQuestionOption> ExamQuestionOptions { get; }

    /// <summary>Prüfungsversuche.</summary>
    DbSet<ExamAttempt> ExamAttempts { get; }

    /// <summary>Einzelantworten eines Prüfungsversuchs.</summary>
    DbSet<ExamAnswer> ExamAnswers { get; }

    /// <summary>Konfigurierbare Finanzkategorien (Einnahme-/Ausgabe-Typen).</summary>
    DbSet<FinanzKategorie> FinanzKategorien { get; }

    /// <summary>Finanzbuchungen (Einnahmen und Ausgaben) je Schule.</summary>
    DbSet<FinanzBuchung> FinanzBuchungen { get; }

    /// <summary>KursNiveau-Durchläufe (Zeiträume für Ratenzahlungen).</summary>
    DbSet<KursNiveauDurchlauf> KursNiveauDurchlaeufe { get; }

    /// <summary>Schülerzahlungen (Cash/Raten) innerhalb eines Durchlaufs.</summary>
    DbSet<RatenZahlung> RatenZahlungen { get; }

    /// <summary>Verbindliche Schülerabschlüsse je KursNiveau-Durchlauf.</summary>
    DbSet<KlassenAbschlussEntscheidung> KlassenAbschlussEntscheidungen { get; }

    /// <summary>Speichert alle ausstehenden Änderungen in der Datenbank.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Liefert das DbSet für den angegebenen Entitätstyp.</summary>
    DbSet<TEntity> Set<TEntity>() where TEntity : class;

    /// <summary>Zugriff auf den Änderungsverfolger (Change Tracker) von EF Core.</summary>
    ChangeTracker ChangeTracker { get; }
}
