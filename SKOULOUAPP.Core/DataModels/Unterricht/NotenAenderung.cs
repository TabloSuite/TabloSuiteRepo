using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels.Unterricht;

/// <summary>Nur ergänzte Historie; bestehende Noten erhalten bei erster Änderung ihren Ausgangswert.</summary>
public class NotenAenderung : AuditableModel
{
    public Guid FachNoteID { get; set; }
    public FachNote FachNote { get; set; } = null!;
    public Guid BearbeitetVonID { get; set; }
    [MaxLength(200)] public string BearbeiterName { get; set; } = string.Empty;
    [Column(TypeName = "decimal(7,2)")] public decimal? AltePunkte { get; set; }
    [Column(TypeName = "decimal(7,2)")] public decimal NeuePunkte { get; set; }
    [MaxLength(300)] public string? AlteBemerkung { get; set; }
    [MaxLength(300)] public string? NeueBemerkung { get; set; }
}
