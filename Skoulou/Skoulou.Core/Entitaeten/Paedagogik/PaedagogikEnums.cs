namespace Skoulou.Core.Entitaeten;

/// <summary>Bearbeitungsstand einer Leçon oder eines Examens.</summary>
public enum InhaltsStatus
{
    Brouillon = 0,
    Prete = 1,

    /// <summary>Aus den Listen genommen (Zuweisungen und Versuche bleiben erhalten).</summary>
    Archive = 2
}

/// <summary>Fragetypen der Bewertungs-Engine.</summary>
public enum FragenTyp
{
    ChoixUnique = 0,
    ChoixMultiple = 1,
    Texte = 2,
    ConstructionPhrase = 3,
    TexteATrous = 4,
    AudioTexte = 5,
    AudioChoixUnique = 6,
    AudioChoixMultiple = 7
}

/// <summary>Welcher Versuch zählt.</summary>
public enum VersuchsWertung
{
    Meilleur = 0,
    Premier = 1,
    Dernier = 2
}

/// <summary>Wann die Lösungen sichtbar werden.</summary>
public enum LoesungsSichtbarkeit
{
    Immediat = 0,
    ApresFrist = 1,
    Jamais = 2
}

/// <summary>Zustand einer Zuweisung.</summary>
public enum ZuweisungsStatus
{
    Active = 0,
    Archivee = 1
}

/// <summary>Zustand eines Versuchs.</summary>
public enum VersuchsStatus
{
    EnCours = 0,
    Soumis = 1,
    ACorriger = 2,
    Corrige = 3
}

/// <summary>Wie eine Antwort bewertet wurde.</summary>
public enum KorrekturArt
{
    Auto = 0,
    Manuelle = 1
}
