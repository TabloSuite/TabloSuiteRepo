using Microsoft.AspNetCore.Components;
using Skoulou.Core.Organisation;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Components.Pages.Organisation;

/// <summary>Freie Stunde im Raster (Klick zum Anlegen eines Kurses).</summary>
public sealed record FreierPlatz(DayOfWeek Tag, TimeOnly Beginn);

/// <summary>
/// Wochenraster eines Stundenplans: am PC als Zeitraster (Montag–Samstag, Stunden proportional),
/// am Handy als Liste je Tag. Optional klickbar (Stunde bearbeiten, freie Stunde belegen).
/// </summary>
public partial class WochenRaster
{
    /// <summary>Pixel je Minute im Zeitraster (60 px je Stunde).</summary>
    private const double PixelJeMinute = 1.0;

    [Parameter, EditorRequired] public IReadOnlyList<StundeZeile> Stunden { get; set; } = [];

    /// <summary>Plan einer Lehrkraft: Klasse statt Lehrkraft anzeigen.</summary>
    [Parameter] public bool MitKlasse { get; set; }

    /// <summary>Montag der angezeigten Woche (zeigt die Daten im Kopf).</summary>
    [Parameter] public DateOnly? Montag { get; set; }

    [Parameter] public DateOnly? Heute { get; set; }

    /// <summary>Samstag immer zeigen (z. B. beim Bearbeiten).</summary>
    [Parameter] public bool ImmerSamstag { get; set; }

    [Parameter] public EventCallback<StundeZeile> OnStunde { get; set; }
    [Parameter] public EventCallback<FreierPlatz> OnFreierPlatz { get; set; }

    private sealed record Block(StundeZeile Stunde, int Spur, int Spuren);

    private IReadOnlyList<DayOfWeek> tage = [];
    private Dictionary<DayOfWeek, List<Block>> bloecke = [];
    private int ersteStunde = 7;
    private int letzteStunde = 17;

    private bool Klickbar => OnStunde.HasDelegate;
    private bool MitPlaetzen => OnFreierPlatz.HasDelegate;
    private double Hoehe => (letzteStunde - ersteStunde) * 60 * PixelJeMinute;

    protected override void OnParametersSet()
    {
        var samstag = ImmerSamstag || Stunden.Any(s => s.Wochentag == DayOfWeek.Saturday);
        var sonntag = Stunden.Any(s => s.Wochentag == DayOfWeek.Sunday);
        tage = Wochentage.Alle.Where(t => t switch
        {
            DayOfWeek.Saturday => samstag,
            DayOfWeek.Sunday => sonntag,
            _ => true
        }).ToList();

        ersteStunde = Math.Min(7, Stunden.Count == 0 ? 7 : Stunden.Min(s => s.Beginn.Hour));
        letzteStunde = Math.Max(17, Stunden.Count == 0 ? 17 : Stunden.Max(s => s.Ende.Minute > 0 ? s.Ende.Hour + 1 : s.Ende.Hour));
        bloecke = tage.ToDictionary(t => t, t => Spuren(Stunden.Where(s => s.Wochentag == t).OrderBy(s => s.Beginn).ThenBy(s => s.Ende).ToList()));
    }

    /// <summary>Überlappende Stunden (Konflikte im Entwurf) nebeneinander statt übereinander.</summary>
    private static List<Block> Spuren(List<StundeZeile> stunden)
    {
        var ergebnis = new List<Block>();
        var gruppe = new List<(StundeZeile S, int Spur)>();
        var gruppenEnde = TimeOnly.MinValue;
        void Abschliessen()
        {
            var anzahl = gruppe.Count == 0 ? 1 : gruppe.Max(g => g.Spur) + 1;
            ergebnis.AddRange(gruppe.Select(g => new Block(g.S, g.Spur, anzahl)));
            gruppe.Clear();
        }
        foreach (var s in stunden)
        {
            if (gruppe.Count > 0 && s.Beginn >= gruppenEnde) Abschliessen();
            if (gruppe.Count == 0) gruppenEnde = s.Ende;
            var spur = 0;
            while (gruppe.Any(g => g.Spur == spur && g.S.Ende > s.Beginn)) spur++;
            gruppe.Add((s, spur));
            if (s.Ende > gruppenEnde) gruppenEnde = s.Ende;
        }
        Abschliessen();
        return ergebnis;
    }

    private string Lage(StundeZeile s, int spur, int spuren)
    {
        var oben = ((s.Beginn.Hour - ersteStunde) * 60 + s.Beginn.Minute) * PixelJeMinute;
        var hoehe = Math.Max(22, (s.Ende - s.Beginn).TotalMinutes * PixelJeMinute - 2);
        var breite = 100.0 / spuren;
        return FormattableString.Invariant($"top:{oben}px;height:{hoehe}px;left:calc({breite * spur}% + 2px);width:calc({breite}% - 4px)");
    }

    private string SlotLage(int stunde)
        => FormattableString.Invariant($"top:{(stunde - ersteStunde) * 60 * PixelJeMinute}px;height:{60 * PixelJeMinute}px");

    /// <summary>Stabile Farbe je Fach (Tokens --sk-kreis-0…5).</summary>
    private static int Farbe(string fach) => (int)((uint)fach.Aggregate(17, (h, c) => unchecked(h * 31 + c)) % 6);

    private static string Zeit(StundeZeile s) => $"{SkoulouZeit(s.Beginn)} – {SkoulouZeit(s.Ende)}";

    private static string SkoulouZeit(TimeOnly t) => Skoulou.Core.Werkzeuge.SkoulouKultur.Uhrzeit(t);

    private string Person(StundeZeile s) => MitKlasse ? s.Klasse : s.Lehrer;

    private string? Datum(DayOfWeek tag)
        => Montag is { } m ? m.AddDays(Wochentage.Index(tag)).ToString("dd/MM", Skoulou.Core.Werkzeuge.SkoulouKultur.Kultur) : null;

    private bool IstHeute(DayOfWeek tag) => Montag is { } m && Heute is { } h && m.AddDays(Wochentage.Index(tag)) == h;

    private Task StundeAsync(StundeZeile s) => OnStunde.InvokeAsync(s);

    private Task PlatzAsync(DayOfWeek tag, int stunde) => OnFreierPlatz.InvokeAsync(new FreierPlatz(tag, new TimeOnly(stunde, 0)));
}
