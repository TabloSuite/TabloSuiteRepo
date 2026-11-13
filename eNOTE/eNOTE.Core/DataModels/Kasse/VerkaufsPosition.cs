namespace ENote.Core.DataModels.Kasse;

/// <summary>Eine Zeile auf dem Bon (eine Leistung in einer Menge, ggf. mit Rabatt).</summary>
public class VerkaufsPosition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VerkaufId { get; set; }
    public Verkauf Verkauf { get; set; } = null!;

    public Guid LeistungId { get; set; }
    public Katalog.Leistung Leistung { get; set; } = null!;

    /// <summary>Snapshot des Namens zum Verkaufszeitpunkt.</summary>
    public string LeistungName { get; set; } = string.Empty;

    /// <summary>Snapshot des Einzelpreises zum Verkaufszeitpunkt.</summary>
    public decimal EinzelPreis { get; set; }
    public int Menge { get; set; } = 1;

    public Guid? RabattId { get; set; }
    public Katalog.Rabatt? Rabatt { get; set; }

    /// <summary>Auf diese Position angewendeter Rabattbetrag (Snapshot).</summary>
    public decimal RabattBetrag { get; set; }

    /// <summary>Zeilensumme nach Rabatt.</summary>
    public decimal ZeilenSumme { get; set; }

    /// <summary>Bei Rückerstattung: die erstattete Originalposition (Menge/Summe hier negativ).</summary>
    public Guid? StornoVonPositionId { get; set; }

    /// <summary>Tag der Leistung, wenn sie während der Anwesenheit schon erfasst war (« Enregistrer sans encaisser »); erscheint auf dem Bon.</summary>
    public DateOnly? ErbrachtAm { get; set; }
}
