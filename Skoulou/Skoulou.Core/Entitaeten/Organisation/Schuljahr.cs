namespace Skoulou.Core.Entitaeten;

/// <summary>Schuljahr einer Schule (« 2026-2027 »); alles Organisatorische hängt daran.</summary>
public class Schuljahr : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public string Bezeichnung { get; set; } = string.Empty;
    public DateOnly Beginn { get; set; }
    public DateOnly Ende { get; set; }
    public SchuljahrStatus Status { get; set; }
    public PeriodenArt PeriodenArt { get; set; }

    /// <summary>Jahrespreis je Schüler, beim Start des Jahres eingefroren.</summary>
    public long? PlattformJahrespreis { get; set; }

    public DateTime? AbgeschlossenAmUtc { get; set; }
    public string? WiedereroeffnetGrund { get; set; }

    public List<Periode> Perioden { get; set; } = [];
}

/// <summary>Periode (Trimester, Semester, Session …) eines Schuljahres.</summary>
public class Periode : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid SchuljahrId { get; set; }
    public int Nr { get; set; }
    public string Titel { get; set; } = string.Empty;
    public DateOnly Beginn { get; set; }
    public DateOnly Ende { get; set; }
    public decimal Jahresgewicht { get; set; } = 1;

    /// <summary>false = « Examen blanc » (zählt nicht fürs Jahr und wird nicht abgerechnet).</summary>
    public bool ZaehltFuerJahr { get; set; } = true;

    public PeriodenStatus Status { get; set; }
}

/// <summary>Stufe (« Niveau », z. B. 6e) mit Nachfolger für Versetzungsvorschläge.</summary>
public class Stufe : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Reihenfolge { get; set; }
    public Guid? NachfolgerId { get; set; }
    public Zyklus? Zyklus { get; set; }
    public bool Aktiv { get; set; } = true;
}

/// <summary>Klasse eines Schuljahres (der Jahreswechsel legt neue Klassen an).</summary>
public class Klasse : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid SchuljahrId { get; set; }
    public Guid? StufeId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Klasse nur für eine Session (Sprachschulen).</summary>
    public Guid? PeriodeId { get; set; }

    public int? Kapazitaet { get; set; }

    /// <summary>Professeur principal.</summary>
    public Guid? KlassenlehrerId { get; set; }

    public string? Raum { get; set; }
    public bool Aktiv { get; set; } = true;
}

/// <summary>Fach (« Matière ») einer Schule.</summary>
public class Fach : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Kuerzel { get; set; }
    public FachGruppe Gruppe { get; set; }
    public int Reihenfolge { get; set; }
    public bool Aktiv { get; set; } = true;
}

/// <summary>Fach in einer Klasse mit Koeffizient.</summary>
public class Klassenfach : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid KlasseId { get; set; }
    public Guid FachId { get; set; }
    public decimal Koeffizient { get; set; } = 1;
    public int? Reihenfolge { get; set; }
}

/// <summary>
/// Lehrauftrag: einzige Quelle dafür, welche Klassen/Schüler ein Lehrer sieht, Notenrecht,
/// Lehrername im Bulletin und Stundenplan.
/// </summary>
public class Lehrauftrag : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid KlassenfachId { get; set; }

    /// <summary>Konto mit Rolle Lehrer, Direktor oder Koordinator.</summary>
    public Guid LehrerId { get; set; }

    public decimal Wochenstunden { get; set; }
}
