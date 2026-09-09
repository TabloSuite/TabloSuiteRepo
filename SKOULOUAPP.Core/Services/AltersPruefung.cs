namespace HalloApp.Core.Services;

/// <summary>
/// Reine, UI-unabhängige Hilfslogik zur Altersberechnung.
/// </summary>
public static class AltersPruefung
{
    /// <summary>
    /// Berechnet das Alter (in vollen Jahren) zu einem Stichtag.
    /// </summary>
    public static int BerechneAlter(DateTime geburtsdatum, DateTime stichtag)
    {
        var alter = stichtag.Year - geburtsdatum.Year;
        if (geburtsdatum.Date > stichtag.AddYears(-alter))
        {
            alter--;
        }

        return alter;
    }

}
