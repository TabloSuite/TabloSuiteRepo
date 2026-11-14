using Skoulou.Core.Entitaeten;

namespace Skoulou.Core.Finanzen;

/// <summary>Buchung für Summen: Richtung und Betrag (immer positiv gespeichert).</summary>
public sealed record BuchungsBetrag(BuchungsRichtung Richtung, long Betrag);

/// <summary>Buchung mit Kategorie (für den Bericht).</summary>
public sealed record KategorieBuchung(Guid KategorieId, string Kategorie, BuchungsRichtung KategorieRichtung, BuchungsRichtung Richtung, long Betrag);

/// <summary>Netto einer Kategorie in ihrer natürlichen Richtung (Gegenbuchungen mindern sie).</summary>
public sealed record KategorieSumme(Guid KategorieId, string Kategorie, BuchungsRichtung Richtung, long Betrag);

/// <summary>
/// Kassenjournal (Bauplan §5 Regel 9): Saldo = Σ Recettes − Σ Dépenses, nur echte Geldbewegungen.
/// Offene Forderungen der Familien und offene SKOULOU-Rechnungen sind eigene Kennzahlen, nie Buchungen.
/// </summary>
public static class Kasse
{
    public static long MitVorzeichen(BuchungsRichtung richtung, long betrag) => richtung == BuchungsRichtung.Recette ? betrag : -betrag;

    public static long Saldo(IEnumerable<BuchungsBetrag> buchungen) => buchungen.Sum(b => MitVorzeichen(b.Richtung, b.Betrag));

    public static (long Recettes, long Depenses) Summen(IEnumerable<BuchungsBetrag> buchungen)
    {
        long ein = 0, aus = 0;
        foreach (var b in buchungen)
            if (b.Richtung == BuchungsRichtung.Recette) ein += b.Betrag; else aus += b.Betrag;
        return (ein, aus);
    }

    /// <summary>Summen je Kategorie; eine Gegenbuchung (andere Richtung als die Kategorie) mindert die Kategorie.</summary>
    public static IReadOnlyList<KategorieSumme> NachKategorie(IEnumerable<KategorieBuchung> buchungen)
        => buchungen
            .GroupBy(b => (b.KategorieId, b.Kategorie, b.KategorieRichtung))
            .Select(g => new KategorieSumme(g.Key.KategorieId, g.Key.Kategorie, g.Key.KategorieRichtung,
                g.Sum(b => b.Richtung == g.Key.KategorieRichtung ? b.Betrag : -b.Betrag)))
            .Where(s => s.Betrag != 0)
            .OrderBy(s => s.Richtung).ThenByDescending(s => s.Betrag).ThenBy(s => s.Kategorie, StringComparer.Ordinal)
            .ToList();
}
