namespace SkoulouApp.WebApp.Data;

/// <summary>
/// Transaktionaler Beleg für die Prüfung unklarer Commit-Ergebnisse.
/// Enthält weder Passwörter noch Importinhalte oder andere Zugangsdaten.
/// </summary>
public sealed class BenutzerVorgangsBestaetigung
{
    public Guid Id { get; set; }
    public DateTime ErstelltAmUtc { get; set; }
}
