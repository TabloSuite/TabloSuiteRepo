using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels.Lessons;

/// <summary>
/// Eine einzelne Seite eines <see cref="TeacherLesson"/>-Kursskripts.
/// Der Inhalt ist bereinigtes HTML (Rich-Text) und darf eingebettete Bilder
/// (data:-URLs) enthalten. Die Seiten entsprechen dem A4-Seitenkonzept:
/// Läuft eine Seite über, legt der Lehrer eine neue Seite an.
/// </summary>
public class TeacherLessonPage : AuditableModel
{
    /// <summary>Zugehöriges Kursskript.</summary>
    public Guid TeacherLessonId { get; set; }
    public TeacherLesson TeacherLesson { get; set; } = null!;

    /// <summary>Seitennummer / Sortierreihenfolge (1-basiert).</summary>
    public int PageNumber { get; set; }

    /// <summary>Bereinigter HTML-Inhalt der Seite.</summary>
    public string ContentHtml { get; set; } = string.Empty;
}
