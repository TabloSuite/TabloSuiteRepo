namespace ENote.Core.DataModels.Kasse;

/// <summary>
/// Ein Zahlungsanteil eines Verkaufs (z. B. 5.000 F bar + 10.000 F Flooz).
/// Revisionssicher: Anbietername wird als Kopie gespeichert, damit Bons auch nach
/// einer Umbenennung des Anbieters unverändert bleiben.
/// </summary>
public class VerkaufZahlung
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VerkaufId { get; set; }
    public Verkauf Verkauf { get; set; } = null!;

    /// <summary>Bar, Karte oder MobileMoney (nie Gemischt).</summary>
    public ZahlungsArt Art { get; set; }

    /// <summary>Auf den Verkauf angerechneter Betrag (bei Bar ohne Rückgeld).</summary>
    public decimal Betrag { get; set; }

    public Guid? MobileMoneyAnbieterId { get; set; }
    public MobileMoneyAnbieter? MobileMoneyAnbieter { get; set; }

    /// <summary>Kopie des Anbieternamens zum Buchungszeitpunkt.</summary>
    public string? AnbieterName { get; set; }

    /// <summary>Optionale Transaktionsreferenz (aus der SMS des Anbieters).</summary>
    public string? Referenz { get; set; }

    public int Reihenfolge { get; set; }
}
