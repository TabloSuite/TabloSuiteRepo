using System.Globalization;

namespace Skoulou.Core.Finanzen;

/// <summary>Ergebnis beim Lesen einer Betragseingabe.</summary>
public enum BetragStatus { Leer, Ok, Format, ZuGross }

/// <summary>
/// Liest einen Betrag in ganzen FCFA (« 150000 », « 150.000 », « 150 000 »). Punkt und Leerzeichen gelten nur als
/// Tausendertrennzeichen, also in Dreiergruppen. Klicktest 21.11.2026: « 15000.00 » wurde vorher still zu
/// 1.500.000 FCFA (alle Nicht-Ziffern entfernt) – jetzt ist das eine Fehleingabe mit Meldung.
/// </summary>
public static class BetragEingabe
{
    public const string MeldungGanz = "Saisissez un montant entier en FCFA (sans centimes).";

    private static readonly char[] Trenner = ['.', ' ', ' ', ' '];

    public static BetragStatus Lies(string? eingabe, out long wert)
    {
        wert = 0;
        var text = (eingabe ?? string.Empty).Trim();
        if (text.Length == 0) return BetragStatus.Leer;
        if (text.Any(c => !char.IsAsciiDigit(c) && Array.IndexOf(Trenner, c) < 0)) return BetragStatus.Format;
        var gruppen = text.Split(Trenner);
        // Mit Trennzeichen: erste Gruppe 1–3 Ziffern, jede weitere genau 3 (« 1.500.000 »); « 15000.00 » oder « 1.5 » sind keine Beträge.
        if (gruppen.Length > 1 && (gruppen[0].Length is < 1 or > 3 || gruppen.Skip(1).Any(g => g.Length != 3)))
            return BetragStatus.Format;
        return long.TryParse(string.Concat(gruppen), NumberStyles.None, CultureInfo.InvariantCulture, out wert)
            ? BetragStatus.Ok
            : BetragStatus.ZuGross;
    }
}
