namespace HalloApp.Core.DataModels.Finanzen;

/// <summary>
/// Systemcodes für vordefinierte Finanzkategorien. Kategorien mit einem gesetzten
/// SystemCode sind eingebaut, nicht löschbar und dienen der automatischen
/// Verbuchung (z. B. Klassen-Ratenzahlungen oder HalloApp-Abo-Zahlungen).
/// Frei angelegte Kategorien besitzen <see cref="Keiner"/>.
/// </summary>
public enum FinanzSystemCode
{
    /// <summary>Frei angelegte (nicht-systemische) Kategorie.</summary>
    Keiner = 0,

    // --- Einnahmen ---
    /// <summary>Schülerzahlung für eine Klasse (inkl. Ratenzahlungen).</summary>
    KursNiveauZahlung = 1,

    /// <summary>Verkauf von Lehrmaterial.</summary>
    VerkaufLehrmaterial = 2,

    /// <summary>Schülerzahlung für ein HalloApp-Abo.</summary>
    HalloAppAboZahlung = 3,

    /// <summary>Anteil der Schule am HalloApp-Abo.</summary>
    HalloAppAboAnteil = 4,

    /// <summary>Automatisch gebuchter Schulleiter-Anteil aus einem HalloApp-Abo.</summary>
    HalloAppAboSchulleiterAnteil = 6,

    /// <summary>Automatisch gebuchter Lehrer-Anteil aus einem HalloApp-Abo.</summary>
    HalloAppAboLehrerAnteil = 7,

    /// <summary>Automatisch gebuchter ProductOwner-Anteil aus einem HalloApp-Abo.</summary>
    HalloAppAboProductOwnerAnteil = 8,

    /// <summary>Sonstige Einnahme.</summary>
    SonstigeEinnahme = 5,

    // --- Ausgaben ---
    /// <summary>Miete.</summary>
    Miete = 100,

    /// <summary>Steuer.</summary>
    Steuer = 101,

    /// <summary>Lehrer-Lohnkosten.</summary>
    LehrerLohnKosten = 102,

    /// <summary>Lehrer-Lohn als HalloApp-Abo.</summary>
    LehrerLohnHalloAppAbo = 103,

    /// <summary>Offene Schulgebühr, die beim Schließen eines Klassenzyklus als Verlust ausgebucht wird.</summary>
    SchulgebuehrVerlust = 104,

    /// <summary>Sonstige Ausgabe.</summary>
    SonstigeAusgabe = 105
}
