using System.ComponentModel.DataAnnotations;

namespace SkoulouApp.Core.Models.Admin;

public class CourseFormModel
{
    public Guid? CourseId { get; set; }

    /// <summary>
    /// Interner Kurzname/Code der Klasse. In der Schulleiter-Verwaltung wird
    /// dieser aus dem konkreten Klassennamen abgeleitet.
    /// </summary>
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Veuillez saisir un titre de cours.")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Veuillez saisir un niveau CECR.")]
    [StringLength(10)]
    public string CEFRGroup { get; set; } = string.Empty;

    [Required(ErrorMessage = "Veuillez saisir une description.")]
    public string Description { get; set; } = string.Empty;

    [Range(1, 100)]
    public int RecommendedLessonCount { get; set; } = 6;

    [Range(0, 1000000, ErrorMessage = "Veuillez saisir un prix valide.")]
    public decimal Price { get; set; } = 10000;

    [Range(1, 10000, ErrorMessage = "Veuillez saisir un nombre de places valide.")]
    public int? MaxPlaetze { get; set; }

    public int CurrentStudentCount { get; set; }

    /// <summary>
    /// Schule, der diese Klasse gehört. Null = globales, festes Sprachschul-System-Niveau.
    /// Für schul-eigene Klassen (Sprachschul-Zusatzkurs bzw. Klassen einer normalen Schule)
    /// wird hier die jeweilige SchulID gesetzt.
    /// </summary>
    public Guid? SchoolId { get; set; }

    /// <summary>Optionale Klassenstufe der konkreten Klasse einer normalen Schule.</summary>
    public Guid? KlassenStufeId { get; set; }

    /// <summary>
    /// Anzeigename der Referenzklasse/-stufe, z. B. CP1 für die konkreten
    /// Klassen CP1A und CP1B. Nur für Anzeige und Filterung.
    /// </summary>
    public string? ReferenceName { get; set; }

    /// <summary>Neue Klassenstufe, die beim Speichern für die eigene Schule angelegt wird.</summary>
    [StringLength(100)]
    public string? NeueKlassenStufe { get; set; }

    /// <summary>
    /// Kennzeichnet ein festes System-Niveau (A1–C). System-Niveaus dürfen nicht
    /// bearbeitet oder gelöscht werden.
    /// </summary>
    public bool IstSystemNiveau { get; set; }

    /// <summary>
    /// Wenn aktiv, werden beim Start eines Klassenzyklus die Schüler der Klasse
    /// automatisch mit dem Standard-Abo SKOULOUAPP (AboTyp.SkoulouApp) vorbereitet.
    /// </summary>
    public bool AutoAssignSkoulouAppAboOnCycleStart { get; set; }

    /// <summary>
    /// UI-Bestätigung: Beim Aktivieren während eines laufenden Zyklus sollen
    /// bestehende Schüler sofort das Abo erhalten.
    /// </summary>
    public bool ConfirmAutoAssignActivationForActiveCycle { get; set; }
}
