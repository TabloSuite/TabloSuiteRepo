using Skoulou.Core.Entitaeten;

namespace Skoulou.Core.Werkzeuge;

/// <summary>
/// Lesbare Nummern der Nummernkreise: Quittung « REC-2026-00042 », Écriture « ECR-2026-00042 »,
/// Bulletin « BUL-2026-00042 », Plattformrechnung « SKL-2026-0001 », Matrikel « {Präfix}{JJ}{0001} ».
/// </summary>
public static class NummernFormat
{
    /// <summary>Gilt der Kreis global (statt je Schule)?</summary>
    public static bool IstGlobal(NummernArt art) => art == NummernArt.Plattformrechnung;

    /// <summary>Formatiert eine Nummer; <paramref name="praefix"/> nur für die Matrikel.</summary>
    public static string Formatiere(NummernArt art, int jahr, long nummer, string? praefix = null)
    {
        if (nummer <= 0) throw new ArgumentOutOfRangeException(nameof(nummer), "La numérotation commence à 1.");
        return art switch
        {
            NummernArt.Quittung => $"REC-{jahr:0000}-{nummer:00000}",
            NummernArt.Ecriture => $"ECR-{jahr:0000}-{nummer:00000}",
            NummernArt.Bulletin => $"BUL-{jahr:0000}-{nummer:00000}",
            NummernArt.Plattformrechnung => $"SKL-{jahr:0000}-{nummer:0000}",
            NummernArt.Matrikel => $"{MatrikelPraefix(praefix)}{jahr % 100:00}{nummer:0000}",
            _ => throw new ArgumentOutOfRangeException(nameof(art))
        };
    }

    /// <summary>Präfix bereinigt (Großbuchstaben/Ziffern, höchstens 6 Zeichen); leer → « SK ».</summary>
    public static string MatrikelPraefix(string? praefix)
    {
        var sauber = new string((praefix ?? string.Empty).Trim().ToUpperInvariant()
            .Where(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9').Take(6).ToArray());
        return sauber.Length == 0 ? "SK" : sauber;
    }

    /// <summary>Vorschlag eines Präfixes aus dem Schulnamen (Initialen, z. B. « CDP »).</summary>
    public static string PraefixAusName(string schulName)
    {
        var woerter = TextNormalisierung.OhneAkzente(schulName).ToUpperInvariant()
            .Split([' ', '-', '\'', '’', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 || w.All(char.IsDigit));
        return MatrikelPraefix(string.Concat(woerter.Select(w => w[0])));
    }
}
