using ENote.Core.DataModels.Kasse;

namespace ENote.WebApp.Data.Seed;

/// <summary>
/// Start-Stammdaten der Mobile-Money-Anbieter (UEMOA-Zone, per Migration eingespielt).
/// Feste IDs, damit Migrationen stabil bleiben. Namen/Farben pflegt der SuperAdmin
/// danach in der Administration (Marken ändern sich, z. B. Togocom → Yas).
/// </summary>
public static class MobileMoneyAnbieterStammdaten
{
    private static readonly DateTime Stand = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    private static MobileMoneyAnbieter A(string id, string land, string name, string farbe, int reihenfolge) => new()
    {
        Id = Guid.Parse(id), Land = land, Name = name, Farbe = farbe, Reihenfolge = reihenfolge,
        IstAktiv = true, CreatedAtUtc = Stand
    };

    public static readonly MobileMoneyAnbieter[] Alle =
    [
        A("7a0d0001-0000-4000-8000-000000000001", "Togo", "Flooz", "#0b5cad", 1),
        A("7a0d0001-0000-4000-8000-000000000002", "Togo", "Mixx by Yas", "#ffc800", 2),

        A("7a0d0002-0000-4000-8000-000000000001", "Bénin", "MTN MoMo", "#ffcb05", 1),
        A("7a0d0002-0000-4000-8000-000000000002", "Bénin", "Moov Money", "#0b5cad", 2),
        A("7a0d0002-0000-4000-8000-000000000003", "Bénin", "Celtiis Cash", "#d71920", 3),

        A("7a0d0003-0000-4000-8000-000000000001", "Sénégal", "Orange Money", "#ff7900", 1),
        A("7a0d0003-0000-4000-8000-000000000002", "Sénégal", "Wave", "#1dc8f2", 2),
        A("7a0d0003-0000-4000-8000-000000000003", "Sénégal", "Mixx by Yas", "#ffc800", 3),

        A("7a0d0004-0000-4000-8000-000000000001", "Côte d'Ivoire", "Orange Money", "#ff7900", 1),
        A("7a0d0004-0000-4000-8000-000000000002", "Côte d'Ivoire", "MTN MoMo", "#ffcb05", 2),
        A("7a0d0004-0000-4000-8000-000000000003", "Côte d'Ivoire", "Moov Money", "#0b5cad", 3),
        A("7a0d0004-0000-4000-8000-000000000004", "Côte d'Ivoire", "Wave", "#1dc8f2", 4),

        A("7a0d0005-0000-4000-8000-000000000001", "Burkina Faso", "Orange Money", "#ff7900", 1),
        A("7a0d0005-0000-4000-8000-000000000002", "Burkina Faso", "Moov Money", "#0b5cad", 2),

        A("7a0d0006-0000-4000-8000-000000000001", "Mali", "Orange Money", "#ff7900", 1),
        A("7a0d0006-0000-4000-8000-000000000002", "Mali", "Moov Money", "#0b5cad", 2),
    ];
}
