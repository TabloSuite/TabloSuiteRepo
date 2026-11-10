using ENote.Core.DataModels.Katalog;

namespace ENote.Core.Services.Katalog;

/// <summary>
/// Bewertung des Lagers: Einkaufswert (gebundenes Geld), Verkaufswert und möglicher Gewinn,
/// wenn alle Stückartikel zum aktuellen Preis verkauft werden.
/// </summary>
public static class LagerRechner
{
    public sealed record Position(
        Guid LeistungId, string Name, decimal Bestand, decimal? Einkaufspreis, decimal Verkaufspreis)
    {
        public decimal? Einkaufswert => Einkaufspreis is decimal e ? Math.Round(e * Bestand, 0) : null;
        public decimal Verkaufswert => Math.Round(Verkaufspreis * Bestand, 0);
        public decimal? Gewinn => Einkaufswert is decimal e ? Verkaufswert - e : null;
    }

    public sealed record Bewertung(IReadOnlyList<Position> Positionen)
    {
        /// <summary>Artikel mit Bestand &gt; 0.</summary>
        public int Artikel => Positionen.Count;
        public decimal Stueck => Positionen.Sum(p => p.Bestand);
        public decimal Einkaufswert => Positionen.Sum(p => p.Einkaufswert ?? 0);
        public decimal Verkaufswert => Positionen.Sum(p => p.Verkaufswert);

        /// <summary>Gewinn nur über Artikel mit bekanntem Einkaufspreis (sonst würde er schöngerechnet).</summary>
        public decimal Gewinn => Positionen.Where(p => p.Gewinn is not null).Sum(p => p.Gewinn!.Value);

        /// <summary>Verkaufswert der Artikel, für die ein Einkaufspreis bekannt ist (Basis der Marge).</summary>
        public decimal VerkaufswertMitEinkauf => Positionen.Where(p => p.Einkaufspreis is not null).Sum(p => p.Verkaufswert);
        public decimal? MargeProzent => VerkaufswertMitEinkauf > 0 ? Math.Round(Gewinn / VerkaufswertMitEinkauf * 100, 0) : null;
        public int OhneEinkaufspreis => Positionen.Count(p => p.Einkaufspreis is null);
    }

    /// <summary>
    /// Berücksichtigt nur aktive Artikel mit Lagerführung und positivem Bestand. Sammel-Artikel
    /// (z. B. „Tissu A (3 m)“) haben keinen eigenen Bestand und werden über ihre Basis bewertet.
    /// </summary>
    public static Bewertung Bewerte(IEnumerable<Leistung> leistungen)
        => new(leistungen
            .Where(l => l.IstAktiv && l.HatStueckzahl && l.Stueckzahl > 0 && !l.IstStandardArtikel)
            .Select(l => new Position(l.Id, l.Name, l.Stueckzahl, l.Kaufpreis is > 0 ? l.Kaufpreis : null, l.Preis))
            .OrderByDescending(p => p.Einkaufswert ?? p.Verkaufswert)
            .ToList());
}
