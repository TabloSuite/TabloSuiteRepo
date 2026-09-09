namespace HalloApp.WebApp.Components.Shared;

public static class ScoreColorHelper
{
    /// <summary>
    /// Liefert eine CSS-Farbe für einen Score in Prozent:
    /// unter 50 % = Rot, genau 50 % = HellRot, über 50 % bis 75 % = HellGrün, über 75 % = Grün.
    /// </summary>
    public static string GetScoreColor(int scorePercent) => scorePercent switch
    {
        < 50 => "#dc2626",   // Rot
        50 => "#f87171",     // HellRot
        <= 75 => "#86efac",  // HellGrün
        _ => "#16a34a"       // Grün
    };
}
