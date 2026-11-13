using System.Text;
using System.Text.RegularExpressions;

namespace Skoulou.Core.Werkzeuge;

/// <summary>
/// Regeln für das Identifiant (Anmeldename): klein, ohne Akzente, 3–40 Zeichen aus [a-z0-9._-],
/// Vorschlag « vorname.nachname », bei Kollision « …2 », « …3 ».
/// </summary>
public static partial class IdentifiantRegel
{
    public const int MinLaenge = 3;
    public const int MaxLaenge = 40;

    public const string MeldungFormat =
        "L'identifiant doit comporter 3 à 40 caractères : lettres minuscules sans accent, chiffres, point, tiret ou trait de soulignement.";

    public const string MeldungVergeben = "Cet identifiant est déjà utilisé. Choisissez-en un autre.";

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$")]
    private static partial Regex FormatRegex();

    [GeneratedRegex(@"\.{2,}")]
    private static partial Regex MehrfachPunkte();

    /// <summary>Normalisiert eine Eingabe (« Éléonore D'Almeida » → « eleonore.d.almeida »).</summary>
    public static string Normalisiere(string? eingabe)
    {
        if (string.IsNullOrWhiteSpace(eingabe)) return string.Empty;
        var text = TextNormalisierung.OhneAkzente(eingabe.Trim()).ToLowerInvariant().Replace("ß", "ss");
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is ' ' or '\'' or '’' or '\t') sb.Append('.');
            else if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-' or '_') sb.Append(c);
        }
        var ergebnis = MehrfachPunkte().Replace(sb.ToString(), ".").Trim('.', '-', '_');
        if (ergebnis.Length > MaxLaenge) ergebnis = ergebnis[..MaxLaenge].Trim('.', '-', '_');
        return ergebnis;
    }

    /// <summary>Erfüllt das (bereits normalisierte) Identifiant Länge und Format?</summary>
    public static bool IstGueltig(string? identifiant)
        => identifiant is { Length: >= MinLaenge and <= MaxLaenge } && FormatRegex().IsMatch(identifiant);

    /// <summary>Grundform « vorname.nachname » (höchstens 37 Zeichen, damit eine Zählnummer passt).</summary>
    public static string Grundform(string? vorname, string? nachname)
    {
        var basis = Normalisiere($"{vorname} {nachname}");
        if (basis.Length < MinLaenge) basis = Normalisiere("compte." + basis);
        if (basis.Length > MaxLaenge - 3) basis = basis[..(MaxLaenge - 3)].Trim('.', '-', '_');
        return basis;
    }

    /// <summary>Erster freier Vorschlag: Grundform, sonst Grundform2, Grundform3 …</summary>
    public static string Vorschlag(string? vorname, string? nachname, Func<string, bool> istVergeben)
    {
        var basis = Grundform(vorname, nachname);
        if (!istVergeben(basis)) return basis;
        for (var i = 2; i < 10_000; i++)
        {
            var kandidat = basis + i;
            if (!istVergeben(kandidat)) return kandidat;
        }
        throw new InvalidOperationException("Aucun identifiant libre n'a été trouvé.");
    }

    /// <summary>Asynchrone Variante für Datenbankprüfungen.</summary>
    public static async Task<string> VorschlagAsync(string? vorname, string? nachname, Func<string, Task<bool>> istVergeben)
    {
        var basis = Grundform(vorname, nachname);
        if (!await istVergeben(basis)) return basis;
        for (var i = 2; i < 10_000; i++)
        {
            var kandidat = basis + i;
            if (!await istVergeben(kandidat)) return kandidat;
        }
        throw new InvalidOperationException("Aucun identifiant libre n'a été trouvé.");
    }
}
