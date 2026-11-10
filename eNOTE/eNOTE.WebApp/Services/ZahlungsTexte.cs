using ENote.Core.DataModels.Kasse;
using ENote.Core.Services.Kasse;

namespace ENote.WebApp.Services;

/// <summary>Einheitliche französische Bezeichnungen für Zahlarten (UI, Bon, Berichte).</summary>
public static class ZahlungsTexte
{
    public static string Name(ZahlungsArt art) => art switch
    {
        ZahlungsArt.Bar => "Espèces",
        ZahlungsArt.Karte => "Carte",
        ZahlungsArt.MobileMoney => "Mobile Money",
        ZahlungsArt.Gemischt => "Mixte",
        _ => art.ToString()
    };

    /// <summary>Bezeichnung eines Anteils: bei Mobile Money der Anbieter (z. B. „Flooz“).</summary>
    public static string Name(ZahlungsAnteil anteil)
        => anteil.Art == ZahlungsArt.MobileMoney && !string.IsNullOrWhiteSpace(anteil.AnbieterName)
            ? anteil.AnbieterName!
            : Name(anteil.Art);

    /// <summary>Kurztext aller Anteile, z. B. „Espèces + Flooz“.</summary>
    public static string Kurz(IEnumerable<ZahlungsAnteil> anteile)
        => string.Join(" + ", anteile.Select(Name).Distinct());
}
