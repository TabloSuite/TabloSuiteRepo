namespace Skoulou.Core.Entitaeten;

/// <summary>Gebührenposten eines Schuljahres (« Inscription », « Scolarité », « APE » …).</summary>
public class GebuehrenPosten : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid SchuljahrId { get; set; }
    public string Bezeichnung { get; set; } = string.Empty;

    /// <summary>Betrag in ganzen FCFA.</summary>
    public long Betrag { get; set; }

    /// <summary>Geltung: Stufe und/oder Klasse; beide null = alle.</summary>
    public Guid? StufeId { get; set; }
    public Guid? KlasseId { get; set; }

    /// <summary>
    /// Nur für eine Periode/Session (Sprachschulen): gilt dann nur für Schüler, die in dieser Session eingeschrieben
    /// sind (effektiver Zeitraum, siehe <c>SessionRegel</c>); null = ganzes Jahr.
    /// </summary>
    public Guid? PeriodeId { get; set; }

    public bool Pflicht { get; set; } = true;
    public int Reihenfolge { get; set; }
    public List<GebuehrenRate> Raten { get; set; } = [];
}

/// <summary>Rate eines Postens mit Fälligkeit (Summe = Postenbetrag).</summary>
public class GebuehrenRate : Entitaet
{
    public Guid PostenId { get; set; }
    public int Nr { get; set; }
    public long Betrag { get; set; }
    public DateOnly Faellig { get; set; }
}

/// <summary>Ermäßigung eines Schülers (« Bourse », « Fratrie » …).</summary>
public class Ermaessigung : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid SchuelerId { get; set; }
    public Guid SchuljahrId { get; set; }
    public Guid? PostenId { get; set; }
    public ErmaessigungsArt Art { get; set; }
    public decimal Wert { get; set; }
    public string Grund { get; set; } = string.Empty;
    public Guid ErfasstVonId { get; set; }
}

/// <summary>Zahlung einer Familie (Storno statt Löschen).</summary>
public class Zahlung : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid SchuelerId { get; set; }
    public Guid SchuljahrId { get; set; }
    public long Betrag { get; set; }
    public DateOnly Datum { get; set; }
    public ZahlungsKanal Kanal { get; set; }
    public string? Referenz { get; set; }

    /// <summary>« REC-2026-00042 », eindeutig je Schule.</summary>
    public string Quittungsnummer { get; set; } = string.Empty;

    public string? Bemerkung { get; set; }
    public Guid ErfasstVonId { get; set; }
    public DateTime? StorniertAmUtc { get; set; }
    public Guid? StorniertVonId { get; set; }
    public string? StornoGrund { get; set; }
}
