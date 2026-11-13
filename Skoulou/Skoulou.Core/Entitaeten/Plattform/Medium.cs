namespace Skoulou.Core.Entitaeten;

/// <summary>Gespeicherte Datei (privat; Auslieferung nur über geprüfte Endpunkte).</summary>
public class Medium : Entitaet
{
    public Guid? SchuleId { get; set; }
    public Guid BesitzerId { get; set; }
    public MedienArt Art { get; set; }
    public MedienZweck Zweck { get; set; }

    /// <summary>Pfad im Speicher: schulen/{schuleId:N}/{zweck}/{mediumId:N}.</summary>
    public string BlobName { get; set; } = string.Empty;

    public string Dateiname { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Serverseitig gezählte Länge.</summary>
    public long Bytes { get; set; }

    public int? DauerSek { get; set; }
    public int? Breite { get; set; }
    public int? Hoehe { get; set; }
}

/// <summary>Verwendung eines Mediums (Löschschutz und Zugriffsprüfung).</summary>
public class MedienNutzung : Entitaet
{
    public Guid MediumId { get; set; }

    /// <summary>Art des Ziels, z. B. « Lecon », « Schule.Logo », « Konto.Foto ».</summary>
    public string Typ { get; set; } = string.Empty;

    public Guid ZielId { get; set; }
}

/// <summary>Anteil eines Responsable an einer bestätigt bezahlten Plattformrechnung (eingefroren).</summary>
public class ResponsableAnteil : Entitaet
{
    public Guid ResponsableId { get; set; }
    public Guid RechnungId { get; set; }
    public Guid SchuleId { get; set; }
    public decimal Prozent { get; set; }

    /// <summary>Ganze FCFA (abgerundet); negativ als Ausgleich einer stornierten Rechnung.</summary>
    public long Betrag { get; set; }

    public Guid? AusgleichFuerId { get; set; }
    public DateTime? AusgezahltAmUtc { get; set; }
    public string? AuszahlungsReferenz { get; set; }
    public Guid? AusgezahltVonId { get; set; }
}
