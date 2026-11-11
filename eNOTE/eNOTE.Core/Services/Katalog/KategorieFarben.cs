using System.Text.RegularExpressions;
using ENote.Core.DataModels.Katalog;

namespace ENote.Core.Services.Katalog;

/// <summary>
/// Farben der Leistungskategorien (Katalog und Kasse): gewählte Farbe der Kategorie,
/// sonst die der Hauptkategorie, sonst automatisch aus der Palette (nach Namen sortiert).
/// </summary>
public static partial class KategorieFarben
{
    /// <summary>Farbe für Leistungen ohne Kategorie.</summary>
    public const string Neutral = "var(--enote-border-strong)";

    /// <summary>Automatische Reihenfolge (unverändert, damit bestehende Läden gleich aussehen).</summary>
    public static readonly IReadOnlyList<string> Palette =
        ["#2547b8", "#0f8f86", "#c2410c", "#7c3aed", "#be185d", "#0369a1", "#4d7c0f", "#b45309"];

    /// <summary>
    /// Wählbare Farben. Alle dunkel genug für Text auf hellem Grund, deshalb keine freie Farbwahl.
    /// </summary>
    public static readonly IReadOnlyList<(string Hex, string Name)> Auswahl =
    [
        ("#2547b8", "Bleu"),
        ("#0369a1", "Bleu ciel"),
        ("#0e7490", "Pétrole"),
        ("#0f8f86", "Turquoise"),
        ("#4d7c0f", "Vert"),
        ("#b45309", "Ambre"),
        ("#c2410c", "Orange"),
        ("#b91c1c", "Rouge"),
        ("#be185d", "Rose"),
        ("#7c3aed", "Violet"),
        ("#854d0e", "Brun"),
        ("#475569", "Ardoise"),
    ];

    [GeneratedRegex("^#[0-9a-f]{6}$")]
    private static partial Regex HexMuster();

    /// <summary>Leer → null (automatisch); sonst kleingeschriebenes #rrggbb oder null bei ungültiger Eingabe.</summary>
    public static string? Normalisiere(string? farbe)
    {
        if (string.IsNullOrWhiteSpace(farbe))
        {
            return null;
        }

        var wert = farbe.Trim().ToLowerInvariant();
        return HexMuster().IsMatch(wert) ? wert : null;
    }

    public static bool IstGueltig(string? farbe) => string.IsNullOrWhiteSpace(farbe) || Normalisiere(farbe) is not null;

    /// <summary>Farbe, die die Kategorie ohne eigene Wahl automatisch erhält.</summary>
    public static string Automatisch(IReadOnlyCollection<LeistungKategorie> kategorien, Guid hauptKategorieId)
    {
        var index = kategorien.Where(k => k.ParentId is null).OrderBy(k => k.Name).Select(k => k.Id).ToList().IndexOf(hauptKategorieId);
        return index < 0 ? Neutral : Palette[index % Palette.Count];
    }

    /// <summary>
    /// Anzeigefarbe einer Kategorie: eigene Farbe, sonst Farbe der Hauptkategorie
    /// (Unterkategorien erben), sonst automatisch.
    /// </summary>
    public static string Ermittle(IReadOnlyCollection<LeistungKategorie> kategorien, Guid? kategorieId)
    {
        if (kategorieId is not Guid id)
        {
            return Neutral;
        }

        var kategorie = kategorien.FirstOrDefault(k => k.Id == id);
        if (kategorie is null)
        {
            return Neutral;
        }

        if (Normalisiere(kategorie.Farbe) is string eigene)
        {
            return eigene;
        }

        if (kategorie.ParentId is Guid parentId)
        {
            var haupt = kategorien.FirstOrDefault(k => k.Id == parentId);
            if (Normalisiere(haupt?.Farbe) is string geerbt)
            {
                return geerbt;
            }

            return Automatisch(kategorien, parentId);
        }

        return Automatisch(kategorien, id);
    }
}
