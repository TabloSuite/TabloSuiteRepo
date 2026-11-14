namespace Skoulou.Core.Entitaeten;

/// <summary>Kategorie des Kassenjournals (SchuleId null = Systemkategorie).</summary>
public class Kategorie : Entitaet
{
    public Guid? SchuleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public BuchungsRichtung Richtung { get; set; }

    /// <summary>« Scolarite », « FactureSkoulou » … für automatische Buchungen.</summary>
    public string? SystemCode { get; set; }

    public bool Aktiv { get; set; } = true;
}

/// <summary>Buchung im Kassenjournal: nur echte Geldbewegungen, nie löschen (Korrektur = Gegenbuchung).</summary>
public class Buchung : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public DateOnly Datum { get; set; }
    public BuchungsRichtung Richtung { get; set; }
    public Guid KategorieId { get; set; }
    public long Betrag { get; set; }
    public string Text { get; set; } = string.Empty;

    /// <summary>« ECR-2026-00042 », eindeutig je Schule.</summary>
    public string Nummer { get; set; } = string.Empty;

    public BuchungsHerkunft Herkunft { get; set; }
    public Guid? HerkunftId { get; set; }
    public Guid? BelegMediumId { get; set; }
    public Guid ErfasstVonId { get; set; }

    /// <summary>Bei einer Gegenbuchung: die Buchung, die sie aufhebt (je Buchung höchstens eine Gegenbuchung).</summary>
    public Guid? StornoVonId { get; set; }
}

/// <summary>Rechnung von SKOULOU an eine Schule für eine Periode.</summary>
public class Plattformrechnung : Entitaet, ISchulgebunden
{
    /// <summary>« SKL-2026-0001 », global eindeutig.</summary>
    public string Nummer { get; set; } = string.Empty;

    public Guid SchuleId { get; set; }
    public Guid SchuljahrId { get; set; }
    public Guid PeriodeId { get; set; }
    public DateOnly Datum { get; set; }
    public DateOnly Faellig { get; set; }
    public int Anzahl { get; set; }
    public long Betrag { get; set; }
    public RechnungsStatus Status { get; set; }
    public string? Grund { get; set; }

    /// <summary>Nachtrag zu einer früheren Rechnung derselben Periode.</summary>
    public Guid? ErgaenztId { get; set; }

    public Guid? BestaetigtVonId { get; set; }
    public DateTime? BestaetigtAmUtc { get; set; }

    /// <summary>Anzahl versandter Zahlungserinnerungen (0, 1 = bei Fälligkeit, 2 = Fälligkeit + 7 Tage).</summary>
    public int Erinnerungen { get; set; }

    public List<Rechnungsposition> Positionen { get; set; } = [];
}

/// <summary>Abgerechneter Schüler (einmal je Periode, über nicht stornierte Positionen eindeutig).</summary>
public class Rechnungsposition : Entitaet
{
    public Guid RechnungId { get; set; }

    /// <summary>Kopie der Periode der Rechnung (für den eindeutigen Index).</summary>
    public Guid PeriodeId { get; set; }

    public Guid SchuelerId { get; set; }
    public Guid EinschreibungId { get; set; }
    public long Betrag { get; set; }
    public string? StorniertGrund { get; set; }
    public DateTime? StorniertAmUtc { get; set; }
}

/// <summary>Zahlungsmeldung der Schule zu einer Plattformrechnung.</summary>
public class Zahlungsmeldung : Entitaet
{
    public Guid RechnungId { get; set; }
    public long Betrag { get; set; }
    public DateOnly Datum { get; set; }
    public string? Referenz { get; set; }
    public Guid? BelegMediumId { get; set; }
    public Guid GemeldetVonId { get; set; }

    /// <summary>Ausgabe « FactureSkoulou » im Kassenjournal der Schule.</summary>
    public Guid? BuchungId { get; set; }
}
