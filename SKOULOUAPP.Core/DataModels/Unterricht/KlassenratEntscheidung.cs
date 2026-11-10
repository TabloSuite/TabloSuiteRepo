using System.ComponentModel.DataAnnotations;
using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels.Unterricht;

/// <summary>Distinction du conseil de classe (ohne Eintrag: Vorschlag aus der Periodenmoyenne).</summary>
public enum KlassenratDistinktion { Keine = 0, TableauHonneur = 1, Encouragements = 2, Felicitations = 3 }

/// <summary>Sanctions du conseil de classe (kombinierbar).</summary>
[Flags]
public enum KlassenratSanktion { Keine = 0, AvertissementTravail = 1, AvertissementConduite = 2, BlameTravail = 4, BlameConduite = 8 }

/// <summary>Décision de fin d'année (nur auf dem letzten Bulletin des Schuljahres).</summary>
public enum Jahresentscheidung { Offen = 0, Admis = 1, Redouble = 2, Exclu = 3 }

/// <summary>Entscheidung des Klassenrats je Schüler und Periode; fließt beim Erstellen in den Bulletin-Snapshot.</summary>
public class KlassenratEntscheidung : AuditableModel
{
    public Guid SchulID { get; set; }
    public Guid PeriodeID { get; set; }
    public BewertungsPeriode Periode { get; set; } = null!;
    public Guid JahresZuordnungID { get; set; }
    public SchuelerJahresZuordnung JahresZuordnung { get; set; } = null!;
    /// <summary><c>null</c> = Vorschlag aus der Moyenne verwenden.</summary>
    public KlassenratDistinktion? Distinktion { get; set; }
    public KlassenratSanktion Sanktionen { get; set; }
    /// <summary>Appréciation du conseil de classe.</summary>
    [MaxLength(300)] public string? Bemerkung { get; set; }
    public Jahresentscheidung Entscheidung { get; set; }
    public Guid BearbeitetVonID { get; set; }
}
