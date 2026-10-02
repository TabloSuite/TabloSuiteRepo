namespace SkoulouApp.Core.DataModels.Finanzen;

/// <summary>
/// Systemcodes für vordefinierte Finanzkategorien. Kategorien mit einem gesetzten
/// SystemCode sind eingebaut, nicht löschbar und dienen der automatischen
/// Verbuchung (z. B. Klassen-Ratenzahlungen oder SkoulouApp-Abo-Zahlungen).
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

    /// <summary>Schülerzahlung für ein SkoulouApp-Abo.</summary>
    SkoulouAppAboZahlung = 3,

    /// <summary>Anteil der Schule am SkoulouApp-Abo.</summary>
    SkoulouAppAboAnteil = 4,

    /// <summary>Automatisch gebuchter Schulleiter-Anteil aus einem SkoulouApp-Abo.</summary>
    SkoulouAppAboSchulleiterAnteil = 6,

    /// <summary>Automatisch gebuchter Lehrer-Anteil aus einem SkoulouApp-Abo.</summary>
    SkoulouAppAboLehrerAnteil = 7,

    /// <summary>Automatisch gebuchter ProductOwner-Anteil aus einem SkoulouApp-Abo.</summary>
    SkoulouAppAboProductOwnerAnteil = 8,

    /// <summary>Sonstige Einnahme.</summary>
    SonstigeEinnahme = 5,

    // --- Ausgaben ---
    /// <summary>Miete.</summary>
    Miete = 100,

    /// <summary>Steuer.</summary>
    Steuer = 101,

    /// <summary>Lehrer-Lohnkosten.</summary>
    LehrerLohnKosten = 102,

    /// <summary>Lehrer-Lohn als SkoulouApp-Abo.</summary>
    LehrerLohnSkoulouAppAbo = 103,

    /// <summary>Offene Schulgebühr, die beim Schließen eines Klassenzyklus als Verlust ausgebucht wird.</summary>
    SchulgebuehrVerlust = 104,

    /// <summary>Sonstige Ausgabe.</summary>
    SonstigeAusgabe = 105
}
