namespace Skoulou.Core.Entitaeten;

/// <summary>Art einer Bewertung (Évaluation).</summary>
public enum BewertungsArt
{
    Interrogation = 0,
    Devoir = 1,
    Composition = 2,
    Oral = 3,
    TP = 4,
    Autre = 5
}

/// <summary>Status einer Note; nur « Notee » (und je nach Schule « Absent » als 0) zählt.</summary>
public enum NotenStatus
{
    Notee = 0,
    AbsentJustifie = 1,
    Absent = 2,
    Dispense = 3,

    /// <summary>Note entfernt (zählt wie fehlend); die Zeile bleibt wegen der Historie bestehen.</summary>
    Leer = 4
}

/// <summary>Auszeichnung des Klassenrats.</summary>
public enum Distinktion
{
    Keine = 0,
    TableauHonneur = 1,
    Encouragements = 2,
    Felicitations = 3
}

/// <summary>Sanktionen des Klassenrats (kombinierbar).</summary>
[Flags]
public enum Sanktionen
{
    Keine = 0,
    AvertissementTravail = 1,
    AvertissementConduite = 2,
    BlameTravail = 4,
    BlameConduite = 8
}

/// <summary>Entscheidung am Jahresende (vom Jahreswechsel ausgeführt).</summary>
public enum JahresEntscheidungsArt
{
    Offen = 0,
    Admis = 1,
    Redouble = 2,
    Exclu = 3,
    Orientation = 4,
    Partie = 5
}

/// <summary>Veröffentlichungsstand einer Klasse in einer Periode.</summary>
public enum VeroeffentlichungsStatus
{
    Publiee = 0,
    EnRectification = 1
}

/// <summary>Status eines Bulletins.</summary>
public enum BulletinStatus
{
    Publie = 0,
    Remplace = 1,
    Retire = 2
}

/// <summary>Protokollierte Übergabe eines Bulletins an die Familie.</summary>
public enum UebergabeArt
{
    Notification = 0,
    Remise = 1
}
