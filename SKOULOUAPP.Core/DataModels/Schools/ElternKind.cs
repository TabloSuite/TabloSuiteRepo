using System.ComponentModel.DataAnnotations;
using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Identity;

namespace SkoulouApp.Core.DataModels.Schools;

/// <summary>
/// Verknüpfung Elternkonto ↔ Schülerkonto derselben Schule (ein Konto = eine Schule).
/// Eltern sehen nur Daten verknüpfter Kinder: veröffentlichte Bulletins, Anwesenheit, Schulgebühren, Quittungen.
/// </summary>
public class ElternKind : AuditableModel
{
    public Guid SchulID { get; set; }
    public Guid ElternID { get; set; }
    public AppUser Eltern { get; set; } = null!;
    public Guid SchuelerID { get; set; }
    public AppUser Schueler { get; set; } = null!;
    /// <summary>Lien de parenté, z. B. « Mère », « Père », « Tuteur ».</summary>
    [MaxLength(40)] public string? Beziehung { get; set; }
    public Guid ErstelltVonID { get; set; }
}
