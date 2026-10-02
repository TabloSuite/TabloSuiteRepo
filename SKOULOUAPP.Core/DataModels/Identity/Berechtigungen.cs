namespace SkoulouApp.Core.DataModels.Identity;

/// <summary>
/// Zentrale Definition aller Berechtigungsnamen (Permissions).
/// Vermeidet magische Strings und stellt sicher, dass Seed, Services und
/// UI-Autorisierung denselben Namen verwenden.
/// </summary>
public static class Berechtigungen
{
    /// <summary>Benutzer blockieren, freigeben und löschen. Nur SuperAdmin.</summary>
    public const string UsersBlock = "users.block";

    /// <summary>Schulen und deren Tarife anlegen/verwalten.</summary>
    public const string SchoolsManage = "schools.manage";

    /// <summary>Lehrer anlegen und der eigenen Schule zuordnen.</summary>
    public const string TeachersManage = "teachers.manage";

    /// <summary>Schüler-Registrierungen freigeben/akzeptieren.</summary>
    public const string StudentsApprove = "students.approve";

    /// <summary>Abo zuweisen und ein Niveau als bezahlt markieren.</summary>
    public const string StudentsSubscription = "students.subscription";

    /// <summary>Bereits bestätigte Zahlungen rückgängig machen. Nur SuperAdmin.</summary>
    public const string PaymentsReverse = "payments.reverse";

    /// <summary>Umsatz einsehen und Abrechnungsperioden abschließen.</summary>
    public const string BillingManage = "billing.manage";

    /// <summary>Kurse, Lektionen und Übungen verwalten.</summary>
    public const string CoursesManage = "courses.manage";

    /// <summary>Fortschritt und Ergebnisse einsehen.</summary>
    public const string ReportsView = "reports.view";

    /// <summary>Alle definierten Berechtigungen (für den SuperAdmin).</summary>
    public static readonly IReadOnlyList<string> Alle =
    [
        UsersBlock,
        SchoolsManage,
        TeachersManage,
        StudentsApprove,
        StudentsSubscription,
        PaymentsReverse,
        BillingManage,
        CoursesManage,
        ReportsView
    ];
}

/// <summary>
/// Zentrale Definition aller Rollennamen.
/// </summary>
public static class Rollen
{
    public const string SuperAdmin = "SuperAdmin";
    public const string SchulLeiter = "SchulLeiter";
    public const string Koordinator = "Koordinator";
    public const string ProduktOwner = "ProduktOwner";
    public const string Lehrer = "Lehrer";
    public const string Schueler = "Schüler";
}
