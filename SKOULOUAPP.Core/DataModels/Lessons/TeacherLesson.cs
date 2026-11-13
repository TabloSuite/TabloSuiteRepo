using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;

namespace SkoulouApp.Core.DataModels.Lessons;

/// <summary>
/// Ein vom Lehrer erstelltes, mehrseitiges Kursskript (Vorlesungsskript) für
/// ein bestimmtes Kursniveau. Unabhängig vom Übungs-<see cref="Lesson"/>-System.
/// Alle Schüler des jeweiligen Niveaus dürfen veröffentlichte Skripte lesen –
/// unabhängig von ihrem Abo.
/// </summary>
public class TeacherLesson : AuditableModel
{
    /// <summary>Kursniveau, zu dem das Skript gehört.</summary>
    public Guid KlasseId { get; set; }
    public Klasse Klasse { get; set; } = null!;

    /// <summary>Autor (Lehrer), der das Skript erstellt hat.</summary>
    public Guid? AuthorId { get; set; }
    public AppUser? Author { get; set; }

    /// <summary>
    /// Konto der Direktion (Schulleitung/Koordination), das das Skript im Namen des
    /// Autors erfasst hat. <c>null</c> = vom Lehrer selbst erfasst. Bleibt erhalten,
    /// wenn der Lehrer den Inhalt später selbst bearbeitet (« Saisi par la direction »).
    /// </summary>
    public Guid? ErfasstVonId { get; set; }
    public AppUser? ErfasstVon { get; set; }

    /// <summary>Titel des Skripts, z. B. "Grammatik: Die Artikel".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Kurze Beschreibung/Zusammenfassung für die Übersicht.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Optionale gemeinsame Überschrift für die Lektionen und Übungen eines
    /// Kurses, z. B. "Exercices du cours : Die Artikel".
    /// </summary>
    public string? CourseTheme { get; set; }

    /// <summary>Sortierreihenfolge innerhalb des Niveaus.</summary>
    public int Order { get; set; }

    /// <summary>Gibt an, ob das Skript für Schüler sichtbar ist.</summary>
    public bool IsPublished { get; set; }

    /// <summary>Gibt an, ob der Autor das Skript mit dem Schulleiter teilt.</summary>
    public bool IsSharedWithSchool { get; set; }

    /// <summary>Gibt an, ob der Autor den PDF-Export seines Skripts erlaubt.</summary>
    public bool IsPdfExportEnabled { get; set; }

    public Guid? FachId { get; set; }
    public Fach? Fach { get; set; }

    /// <summary>Optionales Vorlesungsvideo zur Lektion. Wird ausschließlich über Azure Blob Storage ausgeliefert.</summary>
    public Guid? VideoAssetId { get; set; }
    public BlobMediaAsset? VideoAsset { get; set; }

    /// <summary>Die einzelnen (A4-)Seiten des Skripts.</summary>
    public ICollection<TeacherLessonPage> Pages { get; set; } = new List<TeacherLessonPage>();
}
