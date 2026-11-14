using Skoulou.Core.Entitaeten;

namespace Skoulou.Core.Finanzen;

/// <summary>Rate eines Gebührenpostens.</summary>
public sealed record RatenDaten(int Nr, long Betrag, DateOnly Faellig);

/// <summary>Für den Schüler geltender Gebührenposten mit seinen Raten.</summary>
public sealed record PostenDaten(Guid Id, string Bezeichnung, long Betrag, bool Pflicht, int Reihenfolge, IReadOnlyList<RatenDaten> Raten);

/// <summary>Ermäßigung (PostenId null = auf alle Posten).</summary>
public sealed record ErmaessigungDaten(Guid? PostenId, ErmaessigungsArt Art, decimal Wert);

/// <summary>Nicht stornierte Zahlung.</summary>
public sealed record ZahlungDaten(Guid Id, long Betrag, DateOnly Datum, DateTime ErfasstUtc);

/// <summary>Eine Zeile des Kontos: Rate eines Postens (oder « Reste année précédente »).</summary>
public sealed record KontoZeile(
    string Schluessel, Guid? PostenId, string Bezeichnung, int RateNr, int RatenAnzahl, DateOnly? Faellig,
    long Brutto, long Ermaessigung, long Betrag, long Bezahlt, bool Pflicht, bool EnRetard)
{
    public long Offen => Betrag - Bezahlt;
    public bool IstVortrag => PostenId is null;
}

/// <summary>Teil einer Zahlung, der eine Kontozeile deckt (Guid.Empty = Guthaben des Vorjahres).</summary>
public sealed record Zuordnung(Guid ZahlungId, string Schluessel, long Betrag);

/// <summary>Gebührenkonto eines Schülers für ein Schuljahr (Bauplan §5 Regel 8).</summary>
public sealed record GebuehrenKonto(
    IReadOnlyList<KontoZeile> Zeilen, IReadOnlyList<Zuordnung> Zuordnungen, long Vortrag, long Brutto, long Ermaessigungen,
    long Soll, long Bezahlt, long Offen, long EnRetardBetrag, long Guthaben, DateOnly Stichtag)
{
    /// <summary>Guthaben aus dem Vorjahr (wird wie eine erste Zahlung verteilt).</summary>
    public long VortragGuthaben => Vortrag < 0 ? -Vortrag : 0;

    /// <summary>Rückstand aus dem Vorjahr (« Reste année précédente »).</summary>
    public long VortragRest => Vortrag > 0 ? Vortrag : 0;

    /// <summary>Vortrag ins Folgejahr: positiv = Rest, negativ = Guthaben.</summary>
    public long Saldo => Soll - Bezahlt - VortragGuthaben;

    public bool IstEnRetard => EnRetardBetrag > 0;

    public static GebuehrenKonto Leer(DateOnly stichtag) => GebuehrenRechner.Berechne([], [], [], 0, stichtag, stichtag);
}

/// <summary>
/// Reiner Gebührenrechner: Soll = geltende Posten − Ermäßigungen (je Posten nie &lt; 0), Raten mit Fälligkeit,
/// Zahlungen nach Fälligkeit verteilt (Rückstand des Vorjahres zuerst), « en retard » = fällige, nicht gedeckte Raten,
/// Überzahlung = Guthaben. Ermäßigungen kürzen die spätesten Raten zuerst.
/// </summary>
public static class GebuehrenRechner
{
    public const string VortragSchluessel = "vortrag";
    public const string VortragText = "Reste année précédente";

    /// <param name="vortrag">Saldo der Vorjahre: positiv = Rest (eigene Zeile, sofort fällig), negativ = Guthaben.</param>
    /// <param name="stichtag">Heute (für « en retard »).</param>
    /// <param name="standardFaellig">Fälligkeit eines Postens ohne Raten (Beginn des Schuljahres).</param>
    public static GebuehrenKonto Berechne(
        IReadOnlyList<PostenDaten> posten, IReadOnlyList<ErmaessigungDaten> ermaessigungen, IReadOnlyList<ZahlungDaten> zahlungen,
        long vortrag, DateOnly stichtag, DateOnly standardFaellig)
    {
        var zeilen = new List<Arbeitszeile>();
        foreach (var p in posten.OrderBy(p => p.Reihenfolge).ThenBy(p => p.Bezeichnung, StringComparer.Ordinal).ThenBy(p => p.Id))
        {
            var raten = p.Raten.Count > 0
                ? p.Raten.OrderBy(r => r.Nr).ToList()
                : [new RatenDaten(1, p.Betrag, standardFaellig)];
            var postenZeilen = raten.Select(r => new Arbeitszeile($"{p.Id:N}-{r.Nr}", p, r.Nr, raten.Count, r.Faellig, Math.Max(0, r.Betrag))).ToList();
            var brutto = postenZeilen.Sum(z => z.Brutto);
            var abzug = Math.Min(brutto, PostenErmaessigung(p.Id, brutto, ermaessigungen));
            KuerzeVonHinten(postenZeilen, abzug);
            zeilen.AddRange(postenZeilen);
        }

        // Pauschale Beträge ohne Posten kürzen die spätesten Raten aller Posten.
        var pauschal = ermaessigungen.Where(e => e.PostenId is null && e.Art == ErmaessigungsArt.Montant).Sum(e => Runde(e.Wert));
        KuerzeVonHinten(zeilen.OrderBy(z => z.Faellig).ThenBy(z => z.Posten!.Reihenfolge).ThenBy(z => z.RateNr).ToList(), Math.Max(0, pauschal));

        var geordnet = zeilen.OrderBy(z => z.Faellig).ThenBy(z => z.Posten!.Reihenfolge).ThenBy(z => z.Posten!.Id).ThenBy(z => z.RateNr).ToList();
        if (vortrag > 0) geordnet.Insert(0, new Arbeitszeile(VortragSchluessel, null, 1, 1, null, vortrag));

        // Verteilung: Guthaben des Vorjahres, dann Zahlungen in zeitlicher Reihenfolge, je auf die älteste offene Zeile.
        var zuordnungen = new List<Zuordnung>();
        var quellen = new List<(Guid Id, long Betrag)>();
        if (vortrag < 0) quellen.Add((Guid.Empty, -vortrag));
        quellen.AddRange(zahlungen.OrderBy(z => z.Datum).ThenBy(z => z.ErfasstUtc).ThenBy(z => z.Id).Select(z => (z.Id, Math.Max(0, z.Betrag))));
        var index = 0;
        foreach (var (id, betrag) in quellen)
        {
            var rest = betrag;
            while (rest > 0 && index < geordnet.Count)
            {
                var z = geordnet[index];
                var offen = z.Betrag - z.Bezahlt;
                if (offen <= 0) { index++; continue; }
                var teil = Math.Min(offen, rest);
                z.Bezahlt += teil;
                rest -= teil;
                zuordnungen.Add(new Zuordnung(id, z.Schluessel, teil));
            }
        }

        var ergebnis = geordnet.Select(z => new KontoZeile(
            z.Schluessel, z.Posten?.Id, z.Posten?.Bezeichnung ?? VortragText, z.RateNr, z.RatenAnzahl, z.Faellig,
            z.Brutto, z.Brutto - z.Betrag, z.Betrag, z.Bezahlt, z.Posten?.Pflicht ?? true,
            z.Betrag > z.Bezahlt && (z.Faellig is null || z.Faellig < stichtag))).ToList();

        var soll = ergebnis.Sum(z => z.Betrag);
        var bezahlt = zahlungen.Sum(z => Math.Max(0, z.Betrag));
        var verfuegbar = bezahlt + (vortrag < 0 ? -vortrag : 0);
        return new GebuehrenKonto(
            ergebnis, zuordnungen, vortrag,
            Brutto: ergebnis.Where(z => !z.IstVortrag).Sum(z => z.Brutto),
            Ermaessigungen: ergebnis.Sum(z => z.Ermaessigung),
            Soll: soll, Bezahlt: bezahlt,
            Offen: ergebnis.Sum(z => z.Offen),
            EnRetardBetrag: ergebnis.Where(z => z.EnRetard).Sum(z => z.Offen),
            Guthaben: Math.Max(0, verfuegbar - soll),
            Stichtag: stichtag);
    }

    /// <summary>Ermäßigung eines Postens: eigene Beträge/Prozente plus Prozente ohne Posten (ganze FCFA, kaufmännisch gerundet).</summary>
    public static long PostenErmaessigung(Guid postenId, long brutto, IEnumerable<ErmaessigungDaten> ermaessigungen)
    {
        long summe = 0;
        foreach (var e in ermaessigungen)
        {
            if (e.PostenId is { } id && id != postenId) continue;
            if (e.PostenId is null && e.Art == ErmaessigungsArt.Montant) continue;
            summe += e.Art == ErmaessigungsArt.Pourcentage
                ? Runde(brutto * Math.Clamp(e.Wert, 0, 100) / 100m)
                : Math.Max(0, Runde(e.Wert));
        }
        return Math.Max(0, summe);
    }

    /// <summary>Saldo eines abgeschlossenen Jahres für den Vortrag (positiv = Rest, negativ = Guthaben).</summary>
    public static long Saldo(IReadOnlyList<PostenDaten> posten, IReadOnlyList<ErmaessigungDaten> ermaessigungen,
        IReadOnlyList<ZahlungDaten> zahlungen, long vortrag, DateOnly beginn)
        => Berechne(posten, ermaessigungen, zahlungen, vortrag, beginn, beginn).Saldo;

    private static long Runde(decimal wert) => (long)Math.Round(wert, 0, MidpointRounding.AwayFromZero);

    private static void KuerzeVonHinten(List<Arbeitszeile> zeilen, long abzug)
    {
        for (var i = zeilen.Count - 1; i >= 0 && abzug > 0; i--)
        {
            var teil = Math.Min(zeilen[i].Betrag, abzug);
            zeilen[i].Betrag -= teil;
            abzug -= teil;
        }
    }

    private sealed class Arbeitszeile(string schluessel, PostenDaten? posten, int rateNr, int ratenAnzahl, DateOnly? faellig, long brutto)
    {
        public string Schluessel { get; } = schluessel;
        public PostenDaten? Posten { get; } = posten;
        public int RateNr { get; } = rateNr;
        public int RatenAnzahl { get; } = ratenAnzahl;
        public DateOnly? Faellig { get; } = faellig;
        public long Brutto { get; } = brutto;
        public long Betrag { get; set; } = brutto;
        public long Bezahlt { get; set; }
    }
}
