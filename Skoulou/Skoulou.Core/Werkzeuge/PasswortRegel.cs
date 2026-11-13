namespace Skoulou.Core.Werkzeuge;

/// <summary>Passwortregeln (wie Identity konfiguriert): mindestens 6 Zeichen, eine Ziffer, ein Kleinbuchstabe.</summary>
public static class PasswortRegel
{
    public const int MinLaenge = 6;
    public const int MaxLaenge = 100;
    public const string Hinweis = "Au moins 6 caractères, dont 1 chiffre et 1 lettre minuscule.";

    /// <summary>Meldung bei Verstoß, sonst null.</summary>
    public static string? Pruefe(string? passwort)
    {
        if (string.IsNullOrEmpty(passwort) || passwort.Length < MinLaenge)
            return "Le mot de passe doit comporter au moins 6 caractères.";
        if (passwort.Length > MaxLaenge)
            return "Le mot de passe est trop long (100 caractères au maximum).";
        if (!passwort.Any(char.IsDigit))
            return "Le mot de passe doit contenir au moins un chiffre.";
        if (!passwort.Any(char.IsLower))
            return "Le mot de passe doit contenir au moins une lettre minuscule.";
        return null;
    }
}
