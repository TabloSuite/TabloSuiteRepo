using System.Globalization;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;

namespace Skoulou.Core.Noten;

/// <summary>Gelesene Eingabe einer Rasterzelle: leer (Note entfernen), Punkte oder Status.</summary>
public readonly record struct ZellEingabe(bool Leer, NotenStatus Status, decimal? Punkte)
{
    public static readonly ZellEingabe Entfernen = new(true, NotenStatus.Leer, null);
}

/// <summary>
/// Liest die Eingabe einer Zelle im Notenraster: Zahl mit Komma oder Punkt (höchstens 2 Dezimalen, 0 … Barème),
/// Kürzel « abs » (absent), « aj » (absence justifiée), « disp » (dispensé); leer = Note entfernen.
/// </summary>
public static class NotenEingabe
{
    public static ZellEingabe Lese(string? text, decimal bareme)
    {
        var t = (text ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", string.Empty);
        if (t.Length == 0) return ZellEingabe.Entfernen;
        switch (t)
        {
            case "abs" or "a" or "absent" or "absente":
                return new ZellEingabe(false, NotenStatus.Absent, null);
            case "aj" or "j" or "abj":
                return new ZellEingabe(false, NotenStatus.AbsentJustifie, null);
            case "disp" or "d" or "dispense" or "dispensé" or "dispensée":
                return new ZellEingabe(false, NotenStatus.Dispense, null);
        }
        if (t.EndsWith("/" + bareme.ToString("0.##", CultureInfo.InvariantCulture), StringComparison.Ordinal)) t = t[..t.LastIndexOf('/')];
        if (!decimal.TryParse(t.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var punkte))
            throw new FachFehler("Saisissez une note (ex. 12,5) ou abs, aj, disp.");
        if (punkte > bareme)
            throw new FachFehler($"La note doit être comprise entre 0 et {bareme.ToString("0.##", Werkzeuge.SkoulouKultur.Kultur)}.");
        if (decimal.Round(punkte, 2) != punkte)
            throw new FachFehler("Une note a au plus deux décimales.");
        return new ZellEingabe(false, NotenStatus.Notee, punkte);
    }
}
