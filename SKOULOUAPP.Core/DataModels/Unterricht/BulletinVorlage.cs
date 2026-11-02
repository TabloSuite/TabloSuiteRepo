using System.ComponentModel.DataAnnotations;
using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels.Unterricht;

/// <summary>Kontrollierte Layoutoptionen; keine ausführbaren HTML-Vorlagen.</summary>
public class BulletinVorlage : AuditableModel
{
    public Guid SchulID { get; set; }
    public Schools.School Schule { get; set; } = null!;
    public Guid KlasseID { get; set; }
    public Courses.Klasse Klasse { get; set; } = null!;
    public BewertungsArt Art { get; set; }
    [MaxLength(120)] public string Name { get; set; } = string.Empty;
    [MaxLength(7)] public string AkzentFarbe { get; set; } = "#1d4ed8";
    public bool ZeigeKoeffizienten { get; set; } = true;
    public bool ZeigeRang { get; set; } = true;
    public bool ZeigeVorperioden { get; set; } = true;
    public bool ZeigeUnterschriften { get; set; } = true;
    public int SchriftGroesse { get; set; } = 10;
    public int SeitenRand { get; set; } = 25;
    public bool ZeigeLehrkraefte { get; set; } = true;
    public bool ZeigeAnwesenheit { get; set; } = true;
    public Guid? LogoAssetID { get; set; }
    public Guid? SiegelAssetID { get; set; }
    [MaxLength(300)] public string? FussText { get; set; }
    [MaxLength(100)] public string BlockReihenfolge { get; set; } = "Schueler,Noten,Ergebnisse,Bemerkung,Unterschriften";
}

/// <summary>Unveränderlicher fachlicher und gestalterischer Stand; nur Veröffentlichung darf zurückgenommen werden.</summary>
public class BulletinAusgabe : AuditableModel
{
    public Guid SchulID { get; set; }
    public Schools.School Schule { get; set; } = null!;
    public Guid PeriodeID { get; set; }
    public BewertungsPeriode Periode { get; set; } = null!;
    public Guid JahresZuordnungID { get; set; }
    public SchuelerJahresZuordnung JahresZuordnung { get; set; } = null!;
    public int Version { get; set; }
    public bool IstVeroeffentlicht { get; set; }
    public Guid ErstelltVonID { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    [MaxLength(300)] public string AenderungsGrund { get; set; } = string.Empty;
    [MaxLength(300)] public string? RuecknahmeGrund { get; set; }
    public Guid? ZurueckgezogenVonID { get; set; }
    public DateTime? ZurueckgezogenAmUtc { get; set; }
}
