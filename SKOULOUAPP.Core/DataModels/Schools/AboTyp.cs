namespace SkoulouApp.Core.DataModels.Schools;

/// <summary>
/// Einheitlicher SKOULOUAPP-Abo-Typ für eine Klassenfreischaltung.
/// </summary>
public enum AboTyp
{
    /// <summary>
    /// Standard-SKOULOUAPP-Abo für eine Klasse.
    /// </summary>
    SkoulouApp = 0,
}

/// <summary>
/// Zentrale Standardwerte für Abrechnung und Provisionen.
/// </summary>
public static class SkoulouAppKonstanten
{
    /// <summary>Fallback-Standardpreis für das SKOULOUAPP-Abo pro Klasse in FCFA.</summary>
    public const decimal StandardPreisAbo = 12500m;

    /// <summary>Kommissionsbasis entspricht dem globalen Standardpreis.</summary>
    public const decimal KommissionsBasisPreis = StandardPreisAbo;

    /// <summary>Provision des Schulleiters pro bezahltem SKOULOUAPP-Abo in FCFA.</summary>
    public const decimal StandardProvisionSchulleiterPlus = 500m;

    /// <summary>Provision des Lehrers pro bezahltem SKOULOUAPP-Abo in FCFA.</summary>
    public const decimal StandardProvisionLehrerPlus = 1000m;

    /// <summary>Prozentualer Anteil des Schulleiters.</summary>
    public const decimal StandardProvisionSchulleiterProzent = StandardProvisionSchulleiterPlus / KommissionsBasisPreis * 100m;
    public const decimal StandardProvisionLehrerProzent = StandardProvisionLehrerPlus / KommissionsBasisPreis * 100m;
    public const decimal StandardProvisionProductOwnerProzent = 0m;

    // Kompatibilitätswerte für bestehende Auswertungen. Neue Abos verwenden die
    // zentral konfigurierten Prozentsätze und speichern ihre Beträge als Snapshot.
    public const decimal ProvisionSchulleiterBasis = StandardPreisAbo * StandardProvisionSchulleiterProzent / 100m;
    public const decimal ProvisionLehrerBasis = StandardPreisAbo * StandardProvisionLehrerProzent / 100m;
    public const decimal ProvisionSchulleiterPlus = StandardProvisionSchulleiterPlus;
    public const decimal ProvisionLehrerPlus = StandardProvisionLehrerPlus;
    public const decimal ProvisionProductOwnerBasis = StandardPreisAbo * StandardProvisionProductOwnerProzent / 100m;
    public const decimal ProvisionProductOwnerPlus = KommissionsBasisPreis * StandardProvisionProductOwnerProzent / 100m;

    public static decimal BerechneProvision(decimal preis, decimal prozent)
        => Math.Round(preis * prozent / 100m, 2, MidpointRounding.AwayFromZero);
}
