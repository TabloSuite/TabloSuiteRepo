using System.Security.Cryptography;

namespace Skoulou.Core.Werkzeuge;

/// <summary>
/// Gut lesbare Startpasswörter (8 Zeichen, Kleinbuchstaben und Ziffern, ohne 0/O/1/l/I),
/// mit mindestens zwei Ziffern und zwei Buchstaben – erfüllt die Passwortregeln (Ziffer + Kleinbuchstabe).
/// </summary>
public static class StartPasswort
{
    public const int Laenge = 8;
    public const string Buchstaben = "abcdefghijkmnpqrstuvwxyz";
    public const string Ziffern = "23456789";

    /// <summary>Erzeugt ein neues Startpasswort (kryptografisch zufällig).</summary>
    public static string Erzeuge()
    {
        while (true)
        {
            var zeichen = new char[Laenge];
            for (var i = 0; i < Laenge; i++)
            {
                // Etwa ein Drittel Ziffern: gut tippbar am Handy.
                var quelle = RandomNumberGenerator.GetInt32(3) == 0 ? Ziffern : Buchstaben;
                zeichen[i] = quelle[RandomNumberGenerator.GetInt32(quelle.Length)];
            }
            var text = new string(zeichen);
            if (text.Count(char.IsDigit) >= 2 && text.Count(char.IsLetter) >= 2)
                return text;
        }
    }

    /// <summary>Besteht ein Passwort nur aus gut lesbaren Zeichen?</summary>
    public static bool IstLesbar(string passwort)
        => passwort.All(c => Buchstaben.Contains(c) || Ziffern.Contains(c));
}
