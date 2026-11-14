namespace Skoulou.Core.Organisation;

/// <summary>
/// Klassen, die nur in einer Session laufen (Sprachschulen, <c>Klasse.PeriodeId</c>), enden automatisch mit ihr:
/// der effektive Zeitraum einer Einschreibung ist ihr Schnitt mit der Session – berechnet, nie gespeichert.
/// Abrechnung, Gebührenkonten und Listen nutzen dieselbe Regel; ohne Session gilt die Einschreibung unverändert.
/// </summary>
public static class SessionRegel
{
    /// <summary>Effektiver Beginn: nicht vor dem Beginn der Session.</summary>
    public static DateOnly Beginn(DateOnly von, DateOnly? sessionBeginn)
        => sessionBeginn is { } b && b > von ? b : von;

    /// <summary>Effektives Ende = min(Ende der Einschreibung, Ende der Session); null = offen.</summary>
    public static DateOnly? Ende(DateOnly? bis, DateOnly? sessionEnde)
        => sessionEnde is { } s && (bis is null || s < bis) ? s : bis;

    /// <summary>Schneidet der effektive Zeitraum [von, bis] ∩ Session den Zeitraum [beginn, ende]?</summary>
    public static bool Schneidet(DateOnly von, DateOnly? bis, DateOnly? sessionBeginn, DateOnly? sessionEnde, DateOnly beginn, DateOnly ende)
    {
        var v = Beginn(von, sessionBeginn);
        var b = Ende(bis, sessionEnde);
        return v <= ende && (b is null || (b >= beginn && b >= v));
    }

    /// <summary>Läuft die Einschreibung am Stichtag noch (Session-Klasse: Session nicht beendet)?</summary>
    public static bool Laeuft(DateOnly? bis, DateOnly? sessionEnde, DateOnly stichtag)
        => Ende(bis, sessionEnde) is not { } e || e >= stichtag;
}
