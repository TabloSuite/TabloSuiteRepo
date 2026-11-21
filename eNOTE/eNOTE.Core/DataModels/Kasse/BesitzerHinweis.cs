namespace ENote.Core.DataModels.Kasse;

/// <summary>
/// Ein ungelesener Hinweis für den Besitzer (z. B. wenn der Standard-Artikel
/// „Divers“ verwendet wurde, weil eine Leistung fehlte). Rot auf dem Dashboard,
/// bis der Besitzer ihn öffnet und liest.
/// </summary>
public class BesitzerHinweis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public Salons.Salon Salon { get; set; } = null!;

    /// <summary>Text des Hinweises (z. B. „3× Divers wurde eingesetzt“).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Wer es ausgelöst hat (Kassierer).</summary>
    public Guid AusgeloestVonId { get; set; }
    public string AusgeloestVonName { get; set; } = string.Empty;

    /// <summary>true, bis der Besitzer ihn gelesen hat.</summary>
    public bool IstUngelesen { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;
    public DateTime? GelesenUtc { get; set; }
}
