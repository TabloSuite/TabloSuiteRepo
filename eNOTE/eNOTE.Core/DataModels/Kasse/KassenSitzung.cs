namespace ENote.Core.DataModels.Kasse;

public enum KassenSitzungStatus
{
    Offen = 0,
    Abgeschlossen = 1
}

/// <summary>
/// Eine Kassensitzung eines Kassierers: vom PIN-Start mit bestätigtem
/// Wechselgeld-Startbestand bis zum Kassenabschluss.
/// </summary>
public class KassenSitzung
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public Salons.Salon Salon { get; set; } = null!;
    public Guid KassiererId { get; set; }
    public Identity.AppUser Kassierer { get; set; } = null!;

    public DateTime GestartetUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AbgeschlossenUtc { get; set; }
    public KassenSitzungStatus Status { get; set; } = KassenSitzungStatus.Offen;

    /// <summary>Vom Kassierer beim Start gezählter und bestätigter Bargeldbestand.</summary>
    public decimal Startbestand { get; set; }

    /// <summary>
    /// Eine Kasse je Laden, an genau einem Gerät: Geräteschlüssel (32 Hex, aus dem Browser) des Geräts, an dem diese Kasse
    /// läuft. Andere Geräte können nicht kassieren, sondern die Kasse nur mit Besitzer-PIN übernehmen (« Reprendre la
    /// caisse ici »). Null bei Kassen von vor dem 17.11.2026 – das erste Gerät, das sie betritt, übernimmt sie.
    /// </summary>
    public string? GeraetSchluessel { get; set; }

    // Werte beim Abschluss (Soll/Ist)
    public decimal? EndbestandGezaehlt { get; set; }
    public decimal? BareinnahmenSoll { get; set; }
    public decimal? Differenz { get; set; }
    public string? AbschlussNotiz { get; set; }

    public ICollection<Verkauf> Verkaeufe { get; set; } = new List<Verkauf>();
}
