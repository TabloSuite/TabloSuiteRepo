namespace ENote.Core.Services.Kasse;

/// <summary>Rechenregeln der Rückerstattung (Storno als Gegenbuchung), ohne Datenbank testbar.</summary>
public static class StornoRechner
{
    /// <summary>
    /// Erstattungsbetrag für <paramref name="menge"/> Stück einer Bonzeile: anteilig der Zeilensumme
    /// (Rabatt also anteilig berücksichtigt). Wird die letzte offene Menge erstattet, ist es genau der
    /// noch nicht erstattete Rest – so bleibt nach mehreren Teilerstattungen nie ein Rundungsrest übrig.
    /// </summary>
    public static decimal Betrag(decimal zeilenSumme, int originalMenge, int bereitsErstattetMenge, decimal bereitsErstattetBetrag, int menge)
    {
        var offen = originalMenge - bereitsErstattetMenge;
        if (originalMenge <= 0 || menge <= 0 || menge > offen)
            throw new ArgumentOutOfRangeException(nameof(menge), "Quantité à rembourser invalide.");

        return menge == offen
            ? Math.Round(zeilenSumme - bereitsErstattetBetrag, 2, MidpointRounding.AwayFromZero)
            : Math.Round(zeilenSumme * menge / originalMenge, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Noch erstattbarer Betrag eines Zahlwegs (bezahlt minus bereits darüber erstattet, nie negativ).</summary>
    public static decimal Rest(decimal bezahlt, decimal erstattet) => Math.Max(0m, Math.Round(bezahlt - erstattet, 2));
}
