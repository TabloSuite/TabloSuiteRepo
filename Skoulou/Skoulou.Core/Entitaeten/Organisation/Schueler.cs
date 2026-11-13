namespace Skoulou.Core.Entitaeten;

/// <summary>Schülerdaten (1:1 zum Konto).</summary>
public class SchuelerProfil : Entitaet, ISchulgebunden
{
    public Guid KontoId { get; set; }
    public Guid SchuleId { get; set; }

    /// <summary>Matrikel, eindeutig je Schule (automatisch « {Präfix}{JJ}{0001} », überschreibbar).</summary>
    public string? Matrikel { get; set; }

    public string? Geburtsort { get; set; }
    public string? Nationalitaet { get; set; }
    public bool IstInterne { get; set; }
    public bool IstAffecte { get; set; }
    public string? ElternTelefon { get; set; }
    public string? ElternEmail { get; set; }

    /// <summary>Gesprochenes Feedback bei Übungen.</summary>
    public bool StimmeAus { get; set; }

    /// <summary>Annahme einer Selbstregistrierung durch die Schule.</summary>
    public DateTime? AufnahmeBestaetigtAmUtc { get; set; }

    /// <summary>Zufälliger Code im QR der Schülerkarte (Prüfseite ohne Personendaten); neu erzeugt bei Verlust.</summary>
    public string? KartenToken { get; set; }
}

/// <summary>
/// Einschreibung eines Schülers in eine Klasse eines Schuljahres. Die « aktuelle Klasse »
/// wird nur hieraus abgeleitet; Klassenwechsel = alte schließen + neue anlegen.
/// </summary>
public class Einschreibung : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid SchuelerId { get; set; }
    public Guid SchuljahrId { get; set; }
    public Guid KlasseId { get; set; }
    public DateOnly Von { get; set; }
    public DateOnly? Bis { get; set; }
    public EinschreibungsStatus Status { get; set; }
    public bool IstWiederholer { get; set; }
    public string? Austrittsgrund { get; set; }

    /// <summary>Irrtümliche Einschreibung (zählt nicht für die Abrechnung).</summary>
    public DateTime? StorniertAmUtc { get; set; }
    public string? StornoGrund { get; set; }
}

/// <summary>Verknüpfung Elternteil ↔ Kind derselben Schule.</summary>
public class ElternKind : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid ElternId { get; set; }
    public Guid SchuelerId { get; set; }

    /// <summary>« Mère », « Père », « Tuteur », « Autre ».</summary>
    public string Beziehung { get; set; } = "Autre";
}

/// <summary>Stundenplan einer Klasse mit Gültigkeitszeitraum.</summary>
public class Stundenplan : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid KlasseId { get; set; }
    public DateOnly GueltigAb { get; set; }
    public DateOnly? GueltigBis { get; set; }
    public bool Veroeffentlicht { get; set; }
    public List<StundenplanEintrag> Eintraege { get; set; } = [];
}

/// <summary>Eine Stunde im Wochenraster.</summary>
public class StundenplanEintrag : Entitaet
{
    public Guid StundenplanId { get; set; }
    public Guid LehrauftragId { get; set; }
    public DayOfWeek Wochentag { get; set; }
    public TimeOnly Beginn { get; set; }
    public TimeOnly Ende { get; set; }
    public string? Raum { get; set; }
}

/// <summary>Anwesenheit eines Schülers an einem Tag (Klassenappell).</summary>
public class Anwesenheit : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid EinschreibungId { get; set; }
    public DateOnly Datum { get; set; }
    public AnwesenheitsStatus Status { get; set; }
    public bool Entschuldigt { get; set; }
    public int? Minuten { get; set; }
    public string? Bemerkung { get; set; }
    public Guid ErfasstVonId { get; set; }
}
