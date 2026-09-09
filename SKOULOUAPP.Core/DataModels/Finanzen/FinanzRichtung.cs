namespace HalloApp.Core.DataModels.Finanzen;

/// <summary>
/// Legt fest, ob eine Finanzkategorie bzw. -buchung eine Einnahme oder eine
/// Ausgabe der Schule darstellt.
/// </summary>
public enum FinanzRichtung
{
    /// <summary>Geld fließt in die Schule (z. B. Schülerzahlungen).</summary>
    Einnahme = 0,

    /// <summary>Geld verlässt die Schule (z. B. Miete, Löhne).</summary>
    Ausgabe = 1
}
