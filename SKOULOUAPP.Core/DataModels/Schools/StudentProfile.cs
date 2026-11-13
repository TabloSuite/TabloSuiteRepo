using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
namespace SkoulouApp.Core.DataModels.Schools;

public class StudentProfile : AuditableModel
{
    public Guid AppUserId { get; set; }
    public AppUser AppUser { get; set; } = null!;

    public Guid SchoolId { get; set; }
    public School School { get; set; } = null!;

    public string CurrentLevelCode { get; set; } = string.Empty;

    /// <summary>
    /// Eindeutige aktuelle Klasse des Schülers. Der Anzeige-Code bleibt für UI und
    /// bestehende Texte erhalten, darf aber nicht für Berechtigungsprüfungen dienen.
    /// </summary>
    public Guid? CurrentKlasseId { get; set; }
    public Klasse? CurrentKlasse { get; set; }

    public int CompletedLessonsCount { get; set; }

    /// <summary>
    /// Kennzeichnet Schüler, die die Schule verlassen haben.
    /// Archivierte Schüler bleiben historisch erhalten, werden aber aus aktiven Listen ausgeblendet.
    /// </summary>
    public bool IsArchived { get; set; }
    /// <summary>Annahme einer Selbstregistrierung; bleibt bei späterer Kontosperre erhalten.</summary>
    public DateTime? AufnahmeBestaetigtAmUtc { get; set; }

    /// <summary>Optionale Telefonnummer eines Elternteils bzw. Erziehungsberechtigten.</summary>
    public string? ElternTelefonNummer { get; set; }

    /// <summary>Optionale E-Mail-Adresse eines Elternteils bzw. Erziehungsberechtigten.</summary>
    public string? ElternEmail { get; set; }

    /// <summary>Schulbezogene, normalisierte Matrikelnummer; leer bedeutet noch nicht vergeben.</summary>
    public string? Matrikelnummer { get; set; }
    public string? Geburtsort { get; set; }
    /// <summary>Nationalité für den Bulletin-Kopf (optional).</summary>
    public string? Nationalitaet { get; set; }
    /// <summary>Interne (internat) – im Bulletin « Interne : Oui/Non ».</summary>
    public bool IstInterne { get; set; }
    /// <summary>Affecté(e) par l'État – im Bulletin « Affecté(e) : Oui/Non ».</summary>
    public bool IstAffecte { get; set; }

    /// <summary>Schüler hat die gesprochene Rückmeldung bei Übungen ausgeschaltet.</summary>
    public bool AntwortTonAus { get; set; }
}







