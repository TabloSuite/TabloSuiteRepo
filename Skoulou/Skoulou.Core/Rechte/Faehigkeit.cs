namespace Skoulou.Core.Rechte;

/// <summary>Fachliche Fähigkeiten; jede Service-Methode prüft genau eine davon (Bauplan §3.4).</summary>
public enum Faehigkeit
{
    SchuleVerwalten,
    KontenVerwalten,
    KoordinatorenVerwalten,
    SchuljahrVerwalten,
    KlassenVerwalten,
    StundenplanVerwalten,
    AnwesenheitErfassen,
    NotenErfassen,
    NotenAllerFaecher,
    KlassenratErfassen,
    BulletinsVeroeffentlichen,
    InhalteErstellen,
    InhalteFuerLehrerErfassen,
    Korrigieren,
    FinanzenEinsehen,
    ZahlungenErfassen,
    BuchhaltungErfassen,
    GebuehrenVerwalten,
    RechnungSkoulouBearbeiten,
    PlattformVerwalten,
    SchulenBetreuen
}

/// <summary>Zusatzrechte eines Koordinators (Flags am Konto, gesetzt vom Direktor).</summary>
[Flags]
public enum KoordinatorRechte
{
    Keine = 0,
    FinanzenEinsehen = 1,
    ZahlungenErfassen = 2,
    BuchhaltungErfassen = 4,
    RechnungSkoulou = 8,
    Alle = FinanzenEinsehen | ZahlungenErfassen | BuchhaltungErfassen | RechnungSkoulou
}

/// <summary>Umfang eines Rechts.</summary>
public enum Zugriffsart
{
    /// <summary>Kein Zugriff.</summary>
    Nein = 0,

    /// <summary>Nur lesen.</summary>
    Lesen = 1,

    /// <summary>Nur eigene Daten (Lehrauftrag, eigene Kinder, eigene Schulen, eigene Gebühren).</summary>
    Eigene = 2,

    /// <summary>Voller Zugriff innerhalb der Schule (bzw. plattformweit für den Admin).</summary>
    Ja = 3
}
