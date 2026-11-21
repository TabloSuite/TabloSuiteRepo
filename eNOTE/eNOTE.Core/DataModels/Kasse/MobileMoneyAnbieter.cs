namespace ENote.Core.DataModels.Kasse;

/// <summary>
/// Mobile-Money-Anbieter eines Landes (Plattform-Stammdaten, vom SuperAdmin gepflegt).
/// Bewusst kein Enum: Anbieter und Marken unterscheiden sich je Land und ändern sich.
/// </summary>
public class MobileMoneyAnbieter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>Land laut <see cref="Salons.Laender"/> (französischer Name, z. B. „Togo“).</summary>
    public string Land { get; set; } = string.Empty;

    /// <summary>Markenfarbe als Hex (#RRGGBB) für Kacheln in der Kasse.</summary>
    public string Farbe { get; set; } = "#0d9488";

    public int Reihenfolge { get; set; }
    public bool IstAktiv { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;
}
