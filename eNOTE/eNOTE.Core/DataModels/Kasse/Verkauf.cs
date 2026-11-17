namespace ENote.Core.DataModels.Kasse;

public enum ZahlungsArt
{
    Bar = 0,
    Karte = 1,
    MobileMoney = 2,

    /// <summary>Nur auf Verkaufsebene: mehrere Zahlungsanteile unterschiedlicher Art.</summary>
    Gemischt = 3
}

/// <summary>
/// Ein abgeschlossener Bezahlvorgang (Bon). Revisionssicher: wird nach dem
/// Buchen nicht mehr verändert.
/// </summary>
public class Verkauf
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid KassenSitzungId { get; set; }
    public KassenSitzung KassenSitzung { get; set; } = null!;
    public Guid SalonId { get; set; }

    public DateTime ZeitUtc { get; set; } = DateTime.UtcNow;
    /// <summary>
    /// Zusammenfassung für Listen/Filter: Art des einzigen Zahlungsanteils oder
    /// <see cref="ZahlungsArt.Gemischt"/>. Maßgeblich sind die <see cref="Zahlungen"/>.
    /// </summary>
    public ZahlungsArt ZahlungsArt { get; set; }

    /// <summary>Optionaler Kunde aus der Kundenkartei (bei Rückerstattung vom Original übernommen).</summary>
    public Guid? KundeId { get; set; }
    public Kunde? Kunde { get; set; }

    public decimal Zwischensumme { get; set; }
    public decimal RabattSumme { get; set; }
    public decimal GesamtSumme { get; set; }

    /// <summary>Nur bei Baranteil: vom Kunden bar gegebener Betrag.</summary>
    public decimal? Gegeben { get; set; }

    /// <summary>Nur bei Barzahlung: berechnetes Rückgeld.</summary>
    public decimal? Rueckgeld { get; set; }

    /// <summary>Bei Kartenzahlung: Kassierer hat den externen Kartenvorgang bestätigt.</summary>
    public bool KarteZahlungBestaetigt { get; set; }

    public ICollection<VerkaufsPosition> Positionen { get; set; } = new List<VerkaufsPosition>();

    /// <summary>Zahlungsanteile (1..n); Summe der Beträge = <see cref="GesamtSumme"/>.</summary>
    public ICollection<VerkaufZahlung> Zahlungen { get; set; } = new List<VerkaufZahlung>();

    // ---------- Rückerstattung (Storno als Gegenbuchung) ----------

    /// <summary>
    /// Gesetzt bei einer Rückerstattung: der ursprüngliche Verkauf. Die Rückerstattung ist ein
    /// eigener Bon mit negativen Mengen/Beträgen, so heben sich Umsätze und Bargeld automatisch auf;
    /// der Originalbon bleibt unverändert.
    /// </summary>
    public Guid? StornoVonVerkaufId { get; set; }
    public Verkauf? StornoVonVerkauf { get; set; }

    /// <summary>Pflichtgrund der Rückerstattung (z. B. « Article défectueux »).</summary>
    public string? StornoGrund { get; set; }

    /// <summary>Wer die Rückerstattung erfasst hat (Kopie des Namens).</summary>
    public string? StornoErfasstVonName { get; set; }

    public bool IstStorno => StornoVonVerkaufId is not null;

    /// <summary>
    /// Wer den Bon an der Kasse gebucht hat (Nutzerwunsch 17.11.2026 « Qui encaisse ? »): die Person, die sich auf
    /// diesem Gerät per PIN als Kassierer angemeldet hat. Mehrere Personen können gleichzeitig in derselben offenen
    /// Kasse arbeiten; so bleibt jeder Bon einer Person zugeordnet. Ältere Bons: null (dann gilt der Kassierer der Sitzung).
    /// </summary>
    public Guid? KassiererId { get; set; }

    /// <summary>Name des Kassierers zum Zeitpunkt der Buchung (Kopie, bleibt auch nach Umbenennen/Löschen).</summary>
    public string? KassiererName { get; set; }

    /// <summary>Anzeigename für Bon, Listen und Auswertungen: gebuchter Kassierer, sonst der Kassierer der Sitzung.</summary>
    public string KassiererAnzeige(string? sitzungsKassierer)
        => !string.IsNullOrWhiteSpace(KassiererName) ? KassiererName! : sitzungsKassierer ?? "—";
}
