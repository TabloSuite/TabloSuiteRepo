namespace Skoulou.Core.Entitaeten;

/// <summary>Lebenszyklus eines Schuljahres; höchstens ein Jahr « Laufend » je Schule.</summary>
public enum SchuljahrStatus
{
    Vorbereitung = 0,
    Laufend = 1,
    Abgeschlossen = 2
}

/// <summary>Periodenstruktur eines Schuljahres.</summary>
public enum PeriodenArt
{
    Trimestre = 0,
    Semestre = 1,
    Session = 2,
    Libre = 3
}

/// <summary>Zustand einer Periode.</summary>
public enum PeriodenStatus
{
    Offen = 0,
    Geschlossen = 1
}

/// <summary>Schulstufe einer Stufe (« Niveau »).</summary>
public enum Zyklus
{
    Primaire = 0,
    College = 1,
    Lycee = 2,
    Formation = 3
}

/// <summary>Fachgruppe im Bulletin.</summary>
public enum FachGruppe
{
    Aucune = 0,
    Lettres = 1,
    Sciences = 2,
    Autres = 3
}

/// <summary>Zustand einer Einschreibung; höchstens eine « Active » je Schüler.</summary>
public enum EinschreibungsStatus
{
    Active = 0,
    Changee = 1,
    Partie = 2,
    Terminee = 3
}

/// <summary>Anwesenheit beim Klassenappell.</summary>
public enum AnwesenheitsStatus
{
    Present = 0,
    Absent = 1,
    Retard = 2
}
