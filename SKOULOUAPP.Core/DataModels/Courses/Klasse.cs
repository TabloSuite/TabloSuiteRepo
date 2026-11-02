using SkoulouApp.Core.DataModels.BaseModel;
namespace SkoulouApp.Core.DataModels.Courses;

public class Klasse : AuditableModel
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int RecommendedLessonCount { get; set; } // Empfohlene Anzahl an Lektionen
    /// <summary>
    /// Preis für die Freischaltung dieses Niveaus (in FCFA), vom SuperAdmin festgelegt.
    /// </summary>
    public decimal Price { get; set; } = 12500;

    /// <summary>
    /// Optionale Richtkapazität der Klasse. Überbelegung bleibt erlaubt.
    /// </summary>
    public int? MaxPlaetze { get; set; }

    /// <summary>
    /// Schule, der diese Klasse gehört. Ohne Schule bleiben historische Datensätze
    /// außerhalb der schulbezogenen Klassenverwaltung.
    /// </summary>
    public Guid? SchoolId { get; set; }
    public School? School { get; set; }

    /// <summary>
    /// Optionale schulgebundene Klassenstufe, z. B. CEP1
    /// für konkrete Klassen CEP1A und CEP1B.
    /// </summary>
    public Guid? KlassenStufeId { get; set; }
    public KlassenStufe? KlassenStufe { get; set; }

    /// <summary>
    /// Kennzeichnet geschützte Altdaten, die nicht als reguläre Schulklassen dienen.
    /// </summary>
    public bool IstSystemNiveau { get; set; }

    /// <summary>
    /// Wenn aktiv, werden beim Start eines neuen Klassenzyklus die Schüler der Klasse
    /// automatisch mit dem Standard-Abo (AboTyp.SkoulouApp) vorbereitet.
    /// </summary>
    public bool AutoAssignSkoulouAppAboOnCycleStart { get; set; }

    public ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
    public ICollection<Exam> Exams { get; set; } = new List<Exam>();
    public ICollection<LevelEnrollment> Enrollments { get; set; } = new List<LevelEnrollment>();
}







