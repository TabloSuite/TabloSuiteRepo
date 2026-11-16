namespace ENote.WebApp.Services;

/// <summary>
/// Telefonnummer eines Besitzers: Pflicht bei der Registrierung (öffentlich und durch den Responsable), damit TabloSuite
/// und der Responsable ihn leicht erreichen. Erlaubt sind Ziffern, Leerzeichen und + - . ( ); mindestens 8 Ziffern.
/// </summary>
public static class TelefonRegel
{
    public const int MinZiffern = 8;
    public const int MaxLaenge = 30;
    public const string MeldungPflicht = "Veuillez saisir votre numéro de téléphone : nous vous appelons en cas de besoin.";
    public const string MeldungPflichtBesitzer = "Veuillez saisir le numéro de téléphone du propriétaire, pour le joindre en cas de besoin.";
    public const string MeldungUngueltig = "Le numéro de téléphone ne peut contenir que des chiffres, des espaces et le signe +.";
    public const string MeldungKurz = "Le numéro de téléphone semble incomplet (au moins 8 chiffres).";

    /// <summary>Bereinigte Nummer oder eine französische Fehlermeldung.</summary>
    public static (string? Nummer, string? Fehler) Pruefe(string? eingabe, string meldungPflicht = MeldungPflicht)
    {
        var t = string.Join(' ', (eingabe ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (t.Length == 0) return (null, meldungPflicht);
        if (t.Length > MaxLaenge || t.Any(c => !(char.IsDigit(c) || c is '+' or ' ' or '-' or '.' or '(' or ')'))) return (null, MeldungUngueltig);
        if (t.Count(char.IsDigit) < MinZiffern) return (null, MeldungKurz);
        return (t, null);
    }
}
