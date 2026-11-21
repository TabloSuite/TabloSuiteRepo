namespace ENote.Core.DataModels.Infrastruktur;

/// <summary>
/// Ein-/Aus-Schalter für eine abschaltbare automatische E-Mail-Art (Schlüssel = Kategorie,
/// siehe <c>MailArten</c>). Fehlt ein Eintrag, gilt der Standard der Mail-Art.
/// </summary>
public class MailRegel
{
    public string Kategorie { get; set; } = string.Empty;
    public bool Aktiv { get; set; }
    public DateTime GeaendertUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;
    public Guid? GeaendertVonId { get; set; }
}
