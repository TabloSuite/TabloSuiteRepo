using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels;

public class SchulModell : AuditableModel
{
    public string Name { get; set; } = string.Empty; // Name der Schule.
    public string Stadt { get; set; } = string.Empty; // Stadt, in der sich die Schule befindet.
    public string Land { get; set; } = string.Empty; // Land, in dem sich die Schule befindet.
    public bool IstAktiv { get; set; } = true; // Gibt an, ob die Schule aktiv genutzt werden kann.
}






