using System.ComponentModel.DataAnnotations;
using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels.Unterricht;

public class BulletinAbschluss : AuditableModel
{
    public Guid SchulID { get; set; }
    public Guid PeriodeID { get; set; }
    public BewertungsPeriode Periode { get; set; } = null!;
    public Guid KlasseID { get; set; }
    public Guid ErstelltVonID { get; set; }
}

/// <summary>Separater Entwurf: ändert weder Originalnoten noch veröffentlichte Dokumente.</summary>
public class BulletinBerichtigung : AuditableModel
{
    public Guid SchulID { get; set; }
    public Guid SchuljahrID { get; set; }
    public Guid KlasseID { get; set; }
    public Guid ErstelltVonID { get; set; }
    [MaxLength(300)] public string Grund { get; set; } = "";
    public string EntwurfJson { get; set; } = "[]";
    public string StammdatenEntwurfJson { get; set; } = "[]";
    public string? VorschauHash { get; set; }
    public DateTime? FreigegebenAmUtc { get; set; }
    public Guid? FreigegebenVonID { get; set; }
    public DateTime? AbgebrochenAmUtc { get; set; }
}

public class BulletinUebergabe : AuditableModel
{
    public Guid AusgabeID { get; set; }
    public BulletinAusgabe Ausgabe { get; set; } = null!;
    public Guid BearbeitetVonID { get; set; }
    public bool IstAbholung { get; set; }
    [MaxLength(300)] public string Vermerk { get; set; } = "";
}
