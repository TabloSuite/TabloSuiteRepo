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
    // --- Allgemeines Bulletin-Layout (Côte d'Ivoire / Togo u. a.), seit 11.11.2026 ---
    /// <summary>Spalten je Bewertungsbestandteil (z. B. « Classe », « Compo »).</summary>
    public bool ZeigeTeilnoten { get; set; } = true;
    /// <summary>Spalte « Moy × Coef » und Totaux.</summary>
    public bool ZeigePunkte { get; set; } = true;
    public bool ZeigeFachRang { get; set; } = true;
    public bool ZeigeAppreciation { get; set; } = true;
    /// <summary>Leere Spalte für die Unterschrift der Lehrkraft.</summary>
    public bool ZeigeSignaturSpalte { get; set; }
    /// <summary>Zwischenzeilen « Bilan … » je Fächergruppe.</summary>
    public bool ZeigeFachGruppen { get; set; } = true;
    /// <summary>Résultats de la classe: Durchschnitt, Minimum, Maximum.</summary>
    public bool ZeigeKlassenStatistik { get; set; } = true;
    /// <summary>Distinctions und Sanctions des Klassenrats.</summary>
    public bool ZeigeMentionen { get; set; } = true;
    /// <summary>Jahresbilanz und Décision de fin d'année auf dem letzten Bulletin des Jahres.</summary>
    public bool ZeigeJahresBilanz { get; set; } = true;
    public bool ZeigeFoto { get; set; }
    /// <summary>Moyenne zusätzlich in Worten (« onze virgule deux »).</summary>
    public bool ZeigeNoteInWorten { get; set; }
}

/// <summary>Unveränderlicher fachlicher und gestalterischer Stand; nur Veröffentlichung darf zurückgenommen werden.</summary>
public class BulletinAusgabe : AuditableModel
{
    public bool IstEndgueltig { get; set; }
    [MaxLength(64)] public string? PruefToken { get; set; }
    public Guid? ErsetztAusgabeID { get; set; }
    public Guid? ErsetztDurchID { get; set; }
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
