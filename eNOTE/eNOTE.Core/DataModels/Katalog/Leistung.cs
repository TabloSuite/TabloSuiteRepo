namespace ENote.Core.DataModels.Katalog;

/// <summary>
/// Eine kassierbare Leistung (Dienstleistung wie „Coupe“) oder ein Verkaufsartikel
/// (z. B. Erfrischungsgetränk) mit optionalem Lagerbestand.
/// </summary>
public class Leistung
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public Salons.Salon Salon { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string? Beschreibung { get; set; }

    /// <summary>Art der Leistung: Prestation (Dienstleistung) oder Produit (Verkaufsartikel).</summary>
    public LeistungsTyp Typ { get; set; } = LeistungsTyp.Prestation;

    /// <summary>Verkaufspreis für die Kasse.</summary>
    public decimal Preis { get; set; }

    /// <summary>Optionaler Einkaufs-/Kaufpreis pro Einheit (nur für Produit; für Warenwert und Marge).</summary>
    public decimal? Kaufpreis { get; set; }

    /// <summary>Optionaler Strichcode (EAN/Code), den der Kassierer einscannen kann.</summary>
    public string? StrichCode { get; set; }

    /// <summary>Optionale Kategorie zum Filtern in der Kasse (z. B. „Tissus“, „Boissons“).</summary>
    public Guid? KategorieId { get; set; }
    public LeistungKategorie? Kategorie { get; set; }

    /// <summary>true = Artikel mit Stückzahl/Bestand; false = reine Dienstleistung.</summary>
    public bool HatStueckzahl { get; set; }

    /// <summary>
    /// Aktueller Lagerbestand in der Einheit des Artikels (Stück, Meter, Liter …).
    /// Dezimal erlaubt Teileinheiten (z. B. 2,5 m Stoff).
    /// </summary>
    public decimal Stueckzahl { get; set; }

    /// <summary>Ab diesem Bestand (oder darunter) wird ein „bald leer“-Hinweis gezeigt.</summary>
    public decimal? MindestbestandWarnung { get; set; }

    /// <summary>
    /// Optionaler Link zu einer Basis-Lager-Leistung (z. B. „Stoff A“ mit 10 m).
    /// Sammel-Artikel wie „Stoff A (3 m)“ ziehen beim Verkauf vom Bestand der Basis ab.
    /// </summary>
    public Guid? BasisLeistungId { get; set; }
    public Leistung? BasisLeistung { get; set; }

    /// <summary>
    /// Menge, die ein Verkauf dieses Artikels vom Bestand der <see cref="BasisLeistung"/> abzieht
    /// (z. B. 3 für „Stoff A (3 m)“). Nur relevant, wenn <see cref="BasisLeistungId"/> gesetzt ist.
    /// </summary>
    public decimal? BasisAbzugMenge { get; set; }

    public bool IstAktiv { get; set; } = true;

    /// <summary>
    /// Standard-Artikel „Divers“ (pro Laden automatisch angelegt). Beim Kassieren
    /// legt der Kassierer den Betrag frei fest – für vergessene Leistungen.
    /// </summary>
    public bool IstStandardArtikel { get; set; }

    /// <summary>Optionales Bild aus dem Blob-Speicher.</summary>
    public Guid? BildAssetId { get; set; }
    public Infrastruktur.BlobMediaAsset? BildAsset { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
