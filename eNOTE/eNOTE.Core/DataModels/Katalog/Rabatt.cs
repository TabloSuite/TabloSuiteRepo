namespace ENote.Core.DataModels.Katalog;

public enum RabattArt
{
    Prozent = 0,
    Festbetrag = 1
}

/// <summary>
/// Ein Rabatt, den der Besitzer anlegt und der Kassierer beim Kassieren anwenden
/// kann, sofern die hinterlegte Bedingung erfüllt ist (z. B. Stammkunde).
/// </summary>
public class Rabatt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public Salons.Salon Salon { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    /// <summary>Freitext-Bedingung, die der Kassierer prüft (z. B. „Client fidèle“).</summary>
    public string? Bedingung { get; set; }

    public RabattArt Art { get; set; } = RabattArt.Prozent;

    /// <summary>Prozent (0-100) bei <see cref="RabattArt.Prozent"/>, sonst Festbetrag.</summary>
    public decimal Wert { get; set; }

    /// <summary>Optional auf eine Leistung beschränkt; null = gilt für den ganzen Bon.</summary>
    public Guid? LeistungId { get; set; }
    public Leistung? Leistung { get; set; }

    /// <summary>
    /// Standardmäßig INAKTIV: Der Kassierer muss den Rabatt beim Kassieren explizit
    /// aktivieren. Er gilt, bis der Besitzer ihn löscht (oder die optionale Frist endet).
    /// </summary>
    public bool IstAktiv { get; set; }

    /// <summary>Optionaler Beginn der Gültigkeit (null = unbegrenzt ab sofort).</summary>
    public DateTime? GueltigVonUtc { get; set; }

    /// <summary>Optionales Ende der Gültigkeit (null = unbegrenzt, bis gelöscht).</summary>
    public DateTime? GueltigBisUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
