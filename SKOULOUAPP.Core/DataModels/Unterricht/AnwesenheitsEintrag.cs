using System.ComponentModel.DataAnnotations;
using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels.Unterricht;

public enum AnwesenheitsStatus { Anwesend, Abwesend, Verspaetet }

/// <summary>Eine schulische Anwesenheitsfeststellung pro Schüler, Periode und Datum.</summary>
public class AnwesenheitsEintrag : AuditableModel
{
    public Guid PeriodeID { get; set; }
    public BewertungsPeriode Periode { get; set; } = null!;
    public Guid JahresZuordnungID { get; set; }
    public SchuelerJahresZuordnung JahresZuordnung { get; set; } = null!;
    public DateOnly Datum { get; set; }
    public AnwesenheitsStatus Status { get; set; }
    public bool IstEntschuldigt { get; set; }
    public int VerspaetungsMinuten { get; set; }
    [MaxLength(300)] public string? Bemerkung { get; set; }
    public Guid BearbeitetVonID { get; set; }
}
