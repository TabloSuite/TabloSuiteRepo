namespace HalloApp.Core.Models.Student;

public class StudentDashboardViewModel
{
    public string FullName { get; set; } = string.Empty;
    public string SchoolName { get; set; } = string.Empty;

    public string CurrentLevelCode { get; set; } = string.Empty;
    public bool HasCurrentKlasseAssignment { get; set; }
    public bool HasCurrentClassCycle { get; set; }
    public bool IsCurrentClassCycleClosed { get; set; }
    public bool IsCurrentClassUnlocked { get; set; }
    public bool IsCurrentClassPaid { get; set; }
    public int CompletedLessonsCount { get; set; }
    public bool IsBlocked { get; set; }

    /// <summary>
    /// True, wenn der Schüler alle Lektionen (inkl. Übungen) seines aktuellen Niveaus
    /// abgeschlossen hat und damit die Abschlussprüfung freigeschaltet ist.
    /// </summary>
    public bool CanTakeFinalExam { get; set; }

    /// <summary>True, wenn der Schüler für sein aktuelles Niveau ein HalloPlus-Abo hat.</summary>
    public bool IsHalloPlus { get; set; }

    /// <summary>True, wenn der Schüler mindestens ein bezahltes HALLOAPP-Abo besitzt.</summary>
    public bool HasPaidHalloAppAbo { get; set; }

    /// <summary>True, wenn der Schüler aktuell mindestens eine aktive Klassenfreischaltung hat.</summary>
    public bool HasActiveSubscription { get; set; }

    /// <summary>
    /// Kombinierter Fortschritts-Score aus Lektions-Übungen und Prüfungen (für ALLE Schüler).
    /// </summary>
    public int? CombinedScorePercent { get; set; }

    /// <summary>Anzahl der Datenpunkte (versuchte Übungen + Prüfungen) im kombinierten Score.</summary>
    public int CombinedScoreCount { get; set; }

    /// <summary>Tatsächlich erworbene Punkte aus Lektionsaufgaben (Übungen) der aktuellen Klasse.</summary>
    public int ExercisePointsEarned { get; set; }

    /// <summary>Maximal erreichbare Punkte aus Lektionsaufgaben (Übungen) der aktuellen Klasse.</summary>
    public int ExercisePointsTotal { get; set; }

    /// <summary>Tatsächlich erworbene Punkte aus Examens der aktuellen Klasse.</summary>
    public int ExamPointsEarned { get; set; }

    /// <summary>Maximal erreichbare Punkte aus Examens der aktuellen Klasse.</summary>
    public int ExamPointsTotal { get; set; }

    /// <summary>
    /// Anzahl aufeinanderfolgender Tage (bis heute oder gestern), an denen der Schüler
    /// mindestens eine Übung, Lektion oder Prüfung abgeschlossen/beantwortet hat.
    /// </summary>
    public int StreakDays { get; set; }

    /// <summary>Anzahl freigeschalteter Lektionen der aktuellen Klasse, die noch nicht abgeschlossen sind.</summary>
    public int OpenLessonsCount { get; set; }

    /// <summary>Anzahl veröffentlichter Examens der aktuellen Klasse, die der Schüler noch nicht bestanden hat.</summary>
    public int OpenExamsCount { get; set; }

    public List<StudentCourseProgressItem> CourseProgress { get; set; } = [];
    public List<StudentClassHistoryItem> ClassHistory { get; set; } = [];
}
