using HalloApp.Core.DataModels.BaseModel;
namespace HalloApp.Core.DataModels.Courses;

public class Klasse : AuditableModel
{
    public string Code { get; set; } = string.Empty; // Kurs Level Code (z.B. A1.1, A2.1, B1.1, B2.1, C1.1, C2.1)
    public string Title { get; set; } = string.Empty; // Kurs Level Titel
    public string CEFRGroup { get; set; } = string.Empty; // Kurs Level CEFR Group (A1, A2, B1, B2, C1, C2)
    public string Description { get; set; } = string.Empty; // Kurs Level Beschreibung
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
    /// Schule, der diese Klasse gehört. Ist der Wert null, handelt es sich um ein
    /// globales, festes Sprachschul-Niveau (A1–C), das allen Sprachschulen gemeinsam ist.
    /// Schul-eigene Klassen (Zusatzkurs einer Sprachschule bzw. alle Klassen einer
    /// normalen Schule) tragen hier die jeweilige SchulID.
    /// </summary>
    public Guid? SchoolId { get; set; }
    public School? School { get; set; }

    /// <summary>
    /// Optionale schulgebundene Klassenstufe einer normalen Schule, z. B. CEP1
    /// für konkrete Klassen CEP1A und CEP1B.
    /// </summary>
    public Guid? KlassenStufeId { get; set; }
    public KlassenStufe? KlassenStufe { get; set; }

    /// <summary>
    /// Kennzeichnet ein festes System-Niveau (A1, A2, B1, B2, C) einer Sprachschule.
    /// System-Niveaus dürfen weder bearbeitet noch gelöscht werden.
    /// </summary>
    public bool IstSystemNiveau { get; set; }

    /// <summary>
    /// Wenn aktiv, werden beim Start eines neuen Klassenzyklus die Schüler der Klasse
    /// automatisch mit dem Standard-Abo (AboTyp.HalloApp) vorbereitet.
    /// </summary>
    public bool AutoAssignHalloAppAboOnCycleStart { get; set; }

    public ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
    public ICollection<Exam> Exams { get; set; } = new List<Exam>();
    public ICollection<LevelEnrollment> Enrollments { get; set; } = new List<LevelEnrollment>();
}







