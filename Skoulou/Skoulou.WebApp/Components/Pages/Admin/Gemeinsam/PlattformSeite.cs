using Microsoft.AspNetCore.Components;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Components.Shared;

namespace Skoulou.WebApp.Components.Pages.Admin.Gemeinsam;

/// <summary>Basis der Seiten des Bereichs Plattform: Zeitanzeige in der Zeitzone Togo (UTC gespeichert).</summary>
public abstract class PlattformSeite : SkoulouSeite
{
    [Inject] protected TimeProvider Uhr { get; set; } = default!;

    /// <summary>« 12/11/2026 à 08:30 » (lokal).</summary>
    protected string Zeit(DateTime utc) => new SchulZeit(Uhr).AlsText(utc);

    /// <summary>« 12/11/2026 » (lokal).</summary>
    protected string Tag(DateTime utc) => SkoulouKultur.Datum(new SchulZeit(Uhr).TagVon(utc));
}
