namespace HalloApp.Core.DataModels.Schools;

/// <summary>
/// Einheitlicher HALLOAPP-Abo-Typ für eine Klassenfreischaltung.
/// </summary>
public enum AboTyp
{
    /// <summary>
    /// Kompatibilitätsalias für bestehende Daten und Altaufrufe.
    /// </summary>
    HalloBasis = 0,

    /// <summary>
    /// Kompatibilitätsalias für bestehende Daten und Altaufrufe.
    /// </summary>
    HalloPlus = 0,

    /// <summary>
    /// Standard-HALLOAPP-Abo für eine Klasse.
    /// </summary>
    HalloApp = 0,

    /// <summary>
    /// Sichtbarer SKOULOUAPP-Name für denselben einheitlichen Abo-Typ.
    /// </summary>
    SkoulouApp = 0
}

/// <summary>
/// Zentrale Standardwerte für Abrechnung und Provisionen.
/// </summary>
public static class HalloAppKonstanten
{
    /// <summary>Fallback-Standardpreis für das HALLOAPP-Abo pro Klasse in FCFA.</summary>
    public const decimal StandardPreisAbo = 12500m;

    /// <summary>Kompatibilitätsalias für bestehende Aufrufer.</summary>
    public const decimal StandardPreisHalloBasis = StandardPreisAbo;

    /// <summary>Kompatibilitätsalias für bestehende Aufrufer.</summary>
    public const decimal StandardPreisHalloPlus = StandardPreisAbo;

    /// <summary>Kommissionsbasis entspricht dem globalen Standardpreis.</summary>
    public const decimal KommissionsBasisPreis = StandardPreisAbo;

    /// <summary>Provision des Lehrers pro bezahltem HALLOBASIS-Abo in FCFA.</summary>
    public const decimal StandardProvisionSchulleiterPlus = 500m;

    /// <summary>Provision des Schulleiters pro bezahltem HALLOPLUS-Abo in FCFA.</summary>
    public const decimal StandardProvisionLehrerPlus = 1000m;

    /// <summary>Provision des Lehrers pro bezahltem HALLOPLUS-Abo in FCFA.</summary>
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
