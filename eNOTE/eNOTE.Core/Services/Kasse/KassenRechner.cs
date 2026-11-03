using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;

namespace ENote.Core.Services.Kasse;

/// <summary>Reine Rechenlogik für den Kassenvorgang (testbar, ohne Datenbank).</summary>
public static class KassenRechner
{
    /// <summary>
    /// Wendet einen Rabatt auf einen Betrag an und rundet auf zwei Nachkommastellen.
    /// Bei Prozent: Wert = 0-100. Bei Festbetrag: Wert wird direkt abgezogen,
    /// das Ergebnis fällt nie unter 0.
    /// </summary>
    public static decimal WendeRabattAn(decimal betrag, RabattArt art, decimal wert)
    {
        var rabatt = art switch
        {
            RabattArt.Prozent => betrag * Math.Clamp(wert, 0m, 100m) / 100m,
            RabattArt.Festbetrag => Math.Max(wert, 0m),
            _ => 0m
        };

        var ergebnis = Math.Max(betrag - rabatt, 0m);
        return Math.Round(ergebnis, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Berechnet das Rückgeld bei Barzahlung. Wirft, wenn der gegebene Betrag
    /// die Gesamtsumme nicht deckt.
    /// </summary>
    public static decimal BerechneRueckgeld(decimal gesamtSumme, decimal gegeben)
    {
        if (gegeben < gesamtSumme)
        {
            throw new InvalidOperationException("Der gegebene Betrag ist kleiner als die Gesamtsumme.");
        }

        return Math.Round(gegeben - gesamtSumme, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Erwarteter Bargeldbestand beim Abschluss: Startbestand + alle Bareinnahmen
    /// der Sitzung (Kartenzahlungen erhöhen die Kasse nicht).
    /// </summary>
    public static decimal BerechneBarSoll(decimal startbestand, IEnumerable<Verkauf> verkaeufe)
    {
        var bareinnahmen = verkaeufe
            .Where(v => v.ZahlungsArt == ZahlungsArt.Bar)
            .Sum(v => v.GesamtSumme);

        return Math.Round(startbestand + bareinnahmen, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Differenz zwischen gezähltem Ist- und erwartetem Soll-Bestand.</summary>
    public static decimal BerechneDifferenz(decimal soll, decimal gezaehlt)
        => Math.Round(gezaehlt - soll, 2, MidpointRounding.AwayFromZero);
}
