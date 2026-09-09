namespace HalloApp.Core.Models.Home;

/// <summary>
/// Zusammenfassung des Kurskatalogs für die öffentliche Startseite.
/// Die Werte stammen direkt aus der Datenbank (Klassen, Lektionen, Übungen),
/// damit die Startseite den tatsächlich geseedeten Katalog widerspiegelt.
/// </summary>
public class HomeCatalogViewModel
{
    /// <summary>Anzahl der Kursniveaus/Klassen, die mindestens eine Lektion besitzen.</summary>
    public int NiveauCount { get; set; }

    /// <summary>Gesamtzahl der Lektionen im Katalog.</summary>
    public int LessonCount { get; set; }

    /// <summary>Gesamtzahl der Übungen im Katalog.</summary>
    public int ExerciseCount { get; set; }

    /// <summary>Empfohlene (erste) Lektion für die Vorschau-Karte.</summary>
    public HomeCatalogLessonItem? RecommendedLesson { get; set; }

    /// <summary>Kurze Vorschau der ersten Lektionen.</summary>
    public List<HomeCatalogLessonItem> PreviewLessons { get; set; } = [];

    /// <summary>Kursniveaus mit Lektions- und Übungszahlen für die Kursstruktur.</summary>
    public List<HomeCatalogLevelItem> Levels { get; set; } = [];
}

/// <summary>Ein Kursniveau/eine Klasse mit aggregierten Zahlen für die Startseite.</summary>
public class HomeCatalogLevelItem
{
    public string LevelCode { get; set; } = string.Empty;
    public int LessonCount { get; set; }
    public int ExerciseCount { get; set; }
}

/// <summary>Einzelne Lektion für die Startseiten-Vorschau.</summary>
public class HomeCatalogLessonItem
{
    public string LevelCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int Order { get; set; }
    public int ExerciseCount { get; set; }
    public int MediaCount { get; set; }
}
