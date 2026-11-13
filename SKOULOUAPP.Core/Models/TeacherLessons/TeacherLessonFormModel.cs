using System.ComponentModel.DataAnnotations;

namespace SkoulouApp.Core.Models.TeacherLessons;

/// <summary>
/// Formularmodell zum Erstellen/Bearbeiten eines Kursskripts durch den Lehrer.
/// Enthält die Metadaten sowie die einzelnen (A4-)Seiten.
/// </summary>
public class TeacherLessonFormModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Veuillez choisir un niveau.")]
    public Guid KlasseId { get; set; }

    [Required(ErrorMessage = "Veuillez saisir un titre.")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    public Guid? FachId { get; set; }
    public string? FachName { get; set; }
    public string? NeueFachBezeichnung { get; set; }

    [StringLength(1000)]
    public string Summary { get; set; } = string.Empty;

    [StringLength(200)]
    public string? CourseTheme { get; set; }

    public bool IsPublished { get; set; }

    /// <summary>Neue Lehrmaterialien sind standardmäßig für die Schulleitung sichtbar.</summary>
    public bool IsSharedWithSchool { get; set; } = true;

    /// <summary>Neue Lehrmaterialien erlauben standardmäßig den PDF-Export.</summary>
    public bool IsPdfExportEnabled { get; set; } = true;

    public Guid? VideoAssetId { get; set; }
    public string? VideoDisplayName { get; set; }
    public TimeSpan? VideoDuration { get; set; }
    public string? VideoUrl { get; set; }

    /// <summary>
    /// Nur zur Anzeige (« Saisi par la direction »): wird beim Laden gesetzt und beim
    /// Speichern ignoriert – die Kennzeichnung bestimmt ausschließlich der Server.
    /// </summary>
    public bool IstVonDirektionErfasst { get; set; }

    public List<TeacherLessonPageFormModel> Pages { get; set; } = [];
}

public class TeacherLessonPageFormModel
{
    public Guid? Id { get; set; }
    public int PageNumber { get; set; }
    public string ContentHtml { get; set; } = string.Empty;
}
