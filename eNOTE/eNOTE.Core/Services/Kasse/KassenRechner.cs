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
    /// <summary>
    /// Erwarteter Bargeldbestand inklusive Geldbewegungen in der Kasse:
    /// Startbestand + Baranteile der Verkäufe + Einlagen in die Kasse − Bar-Entnahmen.
    /// </summary>
    public static decimal BerechneBarSoll(decimal startbestand, IEnumerable<Verkauf> verkaeufe, IEnumerable<DataModels.Finanzen.GeldBewegung> bewegungen)
        => Math.Round(BerechneBarSoll(startbestand, verkaeufe)
            + bewegungen.Where(b => b.Ort == DataModels.Finanzen.GeldOrt.Kasse).Sum(b => b.Vorzeichenbetrag),
            2, MidpointRounding.AwayFromZero);

    public static decimal BerechneBarSoll(decimal startbestand, IEnumerable<Verkauf> verkaeufe)
    {
        var bareinnahmen = verkaeufe
            .SelectMany(Anteile)
            .Where(a => a.Art == ZahlungsArt.Bar)
            .Sum(a => a.Betrag);

        return Math.Round(startbestand + bareinnahmen, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Zahlungsanteile eines Verkaufs. Verkäufe ohne erfasste Anteile (Altbestand vor
    /// der Aufteilung) gelten als ein Anteil in Höhe der Gesamtsumme.
    /// </summary>
    public static IReadOnlyList<ZahlungsAnteil> Anteile(Verkauf verkauf)
        => Anteile(verkauf.ZahlungsArt, verkauf.GesamtSumme,
            verkauf.Zahlungen.OrderBy(z => z.Reihenfolge).Select(z => new ZahlungsAnteil(z.Art, z.Betrag, z.AnbieterName)));

    /// <inheritdoc cref="Anteile(Verkauf)"/>
    public static IReadOnlyList<ZahlungsAnteil> Anteile(ZahlungsArt verkaufsArt, decimal gesamtSumme, IEnumerable<ZahlungsAnteil>? zahlungen)
    {
        var liste = zahlungen?.ToList() ?? [];
        return liste.Count > 0
            ? liste
            : [new ZahlungsAnteil(verkaufsArt == ZahlungsArt.Gemischt ? ZahlungsArt.Bar : verkaufsArt, gesamtSumme)];
    }

    /// <summary>Zusammenfassende Verkaufs-Zahlart: die einzige Art oder <see cref="ZahlungsArt.Gemischt"/>.</summary>
    public static ZahlungsArt Zusammenfassen(IEnumerable<ZahlungsArt> arten)
    {
        var verschieden = arten.Distinct().ToList();
        return verschieden.Count == 1 ? verschieden[0] : ZahlungsArt.Gemischt;
    }

    /// <summary>
    /// Prüft die Zahlungsanteile gegen die Gesamtsumme: jeder Anteil positiv, keine
    /// Art „Gemischt“, höchstens ein Baranteil, Summe exakt gleich der Gesamtsumme.
    /// Liefert eine französische Fehlermeldung oder <c>null</c>.
    /// </summary>
    public static string? PruefeZahlungen(decimal gesamtSumme, IReadOnlyList<(ZahlungsArt Art, decimal Betrag)> anteile)
    {
        if (anteile.Count == 0)
            return "Veuillez choisir un mode de paiement.";
        if (anteile.Any(a => a.Art == ZahlungsArt.Gemischt || !Enum.IsDefined(a.Art)))
            return "Mode de paiement invalide.";
        if (anteile.Any(a => a.Betrag <= 0m))
            return "Chaque paiement doit avoir un montant positif.";
        if (anteile.Count(a => a.Art == ZahlungsArt.Bar) > 1)
            return "Un seul paiement en espèces par vente.";
        var summe = Math.Round(anteile.Sum(a => a.Betrag), 2, MidpointRounding.AwayFromZero);
        if (summe != Math.Round(gesamtSumme, 2, MidpointRounding.AwayFromZero))
            return summe < gesamtSumme
                ? $"Il manque {gesamtSumme - summe:N0} F pour couvrir le total."
                : $"Les paiements dépassent le total de {summe - gesamtSumme:N0} F.";
        return null;
    }

    /// <summary>Differenz zwischen gezähltem Ist- und erwartetem Soll-Bestand.</summary>
    public static decimal BerechneDifferenz(decimal soll, decimal gezaehlt)
        => Math.Round(gezaehlt - soll, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Ein Zahlungsanteil für Auswertungen (Art, Betrag, optional Mobile-Money-Anbieter).</summary>
public sealed record ZahlungsAnteil(ZahlungsArt Art, decimal Betrag, string? AnbieterName = null);
