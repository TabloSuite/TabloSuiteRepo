using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels.Unterricht;

public enum BewertungsArt { Allgemein, Trimester, Test }

public class BewertungsPeriode : AuditableModel
{
    public Guid SchulID { get; set; }
    public Guid SchuljahrID { get; set; }
    public Schuljahr Schuljahr { get; set; } = null!;
    [MaxLength(120)] public string Titel { get; set; } = string.Empty;
    public BewertungsArt Art { get; set; }
    public int Reihenfolge { get; set; }
    [Column(TypeName = "decimal(7,2)")] public decimal JahresGewicht { get; set; } = 1;
    public bool IstGeschlossen { get; set; }
}

public class BewertungsBestandteil : AuditableModel
{
    public Guid PeriodeID { get; set; }
    public BewertungsPeriode Periode { get; set; } = null!;
    [MaxLength(80)] public string Bezeichnung { get; set; } = string.Empty;
    [Column(TypeName = "decimal(7,2)")] public decimal Gewicht { get; set; } = 1;
    [Column(TypeName = "decimal(7,2)")] public decimal MaximalPunkte { get; set; } = 20;
}

public class FachBewertung : AuditableModel
{
    public Guid SchuljahrID { get; set; }
    public Schuljahr Schuljahr { get; set; } = null!;
    public Guid KlasseID { get; set; }
    public Courses.Klasse Klasse { get; set; } = null!;
    public Guid FachID { get; set; }
    public Schools.Fach Fach { get; set; } = null!;
    [Column(TypeName = "decimal(7,2)")] public decimal Koeffizient { get; set; } = 1;
}

public class FachNote : AuditableModel
{
    public Guid BestandteilID { get; set; }
    public BewertungsBestandteil Bestandteil { get; set; } = null!;
    public Guid FachBewertungID { get; set; }
    public FachBewertung FachBewertung { get; set; } = null!;
    public Guid JahresZuordnungID { get; set; }
    public SchuelerJahresZuordnung JahresZuordnung { get; set; } = null!;
    [Column(TypeName = "decimal(7,2)")] public decimal Punkte { get; set; }
    [MaxLength(300)] public string? Bemerkung { get; set; }
    public Guid BearbeitetVonID { get; set; }
}
