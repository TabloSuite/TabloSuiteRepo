namespace ENote.Core.DataModels.Katalog;

/// <summary>
/// Art der Leistung: Prestation (Dienstleistung, nur Verkaufspreis) oder
/// Produit (Verkaufsartikel mit Kaufpreis und optionalem Lagerbestand).
/// </summary>
public enum LeistungsTyp
{
    /// <summary>Dienstleistung (z. B. „Coiffure Enfant“). Nur Verkaufspreis, kein Kaufpreis, kein Stock.</summary>
    Prestation = 0,

    /// <summary>Verkaufsartikel (z. B. „Cola“). Kaufpreis + Verkaufspreis (Marge), optional Stock.</summary>
    Produit = 1
}
