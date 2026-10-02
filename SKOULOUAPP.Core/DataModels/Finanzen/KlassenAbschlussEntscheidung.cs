using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Identity;

namespace SkoulouApp.Core.DataModels.Finanzen;

/// <summary>
/// Dokumentiert den individuellen Abschluss eines Schülers in einem Kursdurchlauf.
/// Die Abschlussnote wird manuell als Freitext durch die Schulleitung gepflegt;
/// die Bestanden-Entscheidung wird davon unabhängig verbindlich festgelegt.
/// </summary>
public class KlassenAbschlussEntscheidung : AuditableModel
{
    public Guid DurchlaufId { get; set; }
    public KursNiveauDurchlauf Durchlauf { get; set; } = null!;

    public Guid StudentId { get; set; }
    public AppUser Student { get; set; } = null!;

    /// <summary>Legacy-Feld für frühere numerische Abschlussnoten.</summary>
    public decimal? AbschlussNote { get; set; }

    /// <summary>Manuell gepflegte Abschlussnote im freien Format (z. B. 10/20, 16 de Moyenne, 10/10).</summary>
    public string? AbschlussNoteText { get; set; }

    public string? MentionText { get; set; }

    /// <summary>Null, solange die zuständige Lehrkraft bzw. Schulleitung noch nicht entschieden hat.</summary>
    public bool? IsBestanden { get; set; }

    public DateTime? EntscheidetAmUtc { get; set; }
    public Guid? EntscheidetVonUserId { get; set; }
    public AppUser? EntscheidetVon { get; set; }
}
