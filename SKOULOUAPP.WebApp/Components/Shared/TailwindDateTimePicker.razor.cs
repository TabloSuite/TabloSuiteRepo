using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace HalloApp.WebApp.Components.Shared;

public partial class TailwindDateTimePicker : ComponentBase
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly string[] WochentagKuerzel = ["lun", "mar", "mer", "jeu", "ven", "sam", "dim"];

    [Parameter]
    public DateTime? Value { get; set; }

    [Parameter]
    public EventCallback<DateTime?> ValueChanged { get; set; }

    [Parameter]
    public string? Placeholder { get; set; }

    /// <summary>Wenn false, wird nur ein Datum ohne Uhrzeit ausgewählt.</summary>
    [Parameter]
    public bool ShowTime { get; set; } = true;

    [Parameter]
    public bool IsDisabled { get; set; }

    [Parameter]
    public bool OpenUp { get; set; }

    private bool isOpen;
    private DateTime viewMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private int stunde;
    private int minute;

    private string EffektiverPlatzhalter => Placeholder ?? (ShowTime ? "jj.mm.aaaa --:--" : "jj.mm.aaaa");

    private string MonatLabel
    {
        get
        {
            var label = viewMonth.ToString("MMMM yyyy", Culture);
            return char.ToUpper(label[0], Culture) + label[1..];
        }
    }

    private IEnumerable<DateTime> Tage
    {
        get
        {
            var ersterDesMonats = viewMonth;
            var versatz = ((int)ersterDesMonats.DayOfWeek + 6) % 7; // Montag = 0
            var start = ersterDesMonats.AddDays(-versatz);
            for (var i = 0; i < 42; i++)
            {
                yield return start.AddDays(i);
            }
        }
    }

    protected override void OnParametersSet()
    {
        if (Value is { } v)
        {
            stunde = v.Hour;
            minute = v.Minute;
        }
    }

    private void Toggle()
    {
        if (IsDisabled)
        {
            return;
        }

        isOpen = !isOpen;
        if (isOpen)
        {
            var basis = Value ?? DateTime.Today;
            viewMonth = new DateTime(basis.Year, basis.Month, 1);
        }
    }

    private void Close() => isOpen = false;

    private void PrevMonth() => viewMonth = viewMonth.AddMonths(-1);

    private void NextMonth() => viewMonth = viewMonth.AddMonths(1);

    private async Task SelectDayAsync(DateTime tag)
    {
        if (IsDisabled)
        {
            return;
        }

        viewMonth = new DateTime(tag.Year, tag.Month, 1);
        var std = ShowTime ? stunde : 0;
        var min = ShowTime ? minute : 0;
        var neu = new DateTime(tag.Year, tag.Month, tag.Day, std, min, 0, DateTimeKind.Local);
        await SetValueAsync(neu);
        if (!ShowTime)
        {
            isOpen = false;
        }
    }

    private async Task OnStundeChanged(ChangeEventArgs e)
    {
        if (IsDisabled)
        {
            return;
        }

        if (int.TryParse(e.Value?.ToString(), out var wert))
        {
            stunde = Math.Clamp(wert, 0, 23);
            await ApplyTimeAsync();
        }
    }

    private async Task OnMinuteChanged(ChangeEventArgs e)
    {
        if (IsDisabled)
        {
            return;
        }

        if (int.TryParse(e.Value?.ToString(), out var wert))
        {
            minute = Math.Clamp(wert, 0, 59);
            await ApplyTimeAsync();
        }
    }

    private async Task ApplyTimeAsync()
    {
        var datum = Value?.Date ?? DateTime.Today;
        var neu = new DateTime(datum.Year, datum.Month, datum.Day, stunde, minute, 0, DateTimeKind.Local);
        await SetValueAsync(neu);
    }

    private async Task HeuteAsync()
    {
        if (IsDisabled)
        {
            return;
        }

        var jetzt = DateTime.Now;
        stunde = jetzt.Hour;
        minute = jetzt.Minute;
        viewMonth = new DateTime(jetzt.Year, jetzt.Month, 1);
        var wert = ShowTime ? jetzt : jetzt.Date;
        await SetValueAsync(wert);
    }

    private async Task ClearAsync()
    {
        if (IsDisabled)
        {
            return;
        }

        await SetValueAsync(null);
        isOpen = false;
    }

    private async Task AccepterAsync()
    {
        if (IsDisabled)
        {
            return;
        }

        if (Value is null)
        {
            await SetValueAsync(new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, stunde, minute, 0, DateTimeKind.Local));
        }

        isOpen = false;
    }

    private async Task SetValueAsync(DateTime? neu)
    {
        Value = neu;
        await ValueChanged.InvokeAsync(neu);
    }

    private static string ZellenKlasse(bool istAktuellerMonat, bool istAusgewaehlt, bool istHeute)
    {
        const string basis = "flex h-9 w-9 items-center justify-center rounded-lg text-sm transition";
        if (istAusgewaehlt)
        {
            return $"{basis} bg-indigo-600 font-bold text-white";
        }

        var farbe = istAktuellerMonat ? "text-slate-700 hover:bg-indigo-50" : "text-slate-300 hover:bg-slate-50";
        var rahmen = istHeute ? " ring-1 ring-indigo-400" : string.Empty;
        return $"{basis} {farbe}{rahmen}";
    }
}
