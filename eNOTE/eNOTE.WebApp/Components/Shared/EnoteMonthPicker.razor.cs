using Microsoft.AspNetCore.Components;

namespace ENote.WebApp.Components.Shared;

/// <summary>Moderner Monats-Picker (Jahr navigieren + Monat wählen, @bind-Value = 1. des Monats).</summary>
public partial class EnoteMonthPicker
{
    [Parameter] public DateTime? Value { get; set; }
    [Parameter] public EventCallback<DateTime?> ValueChanged { get; set; }

    private int angezeigtesJahr = DateTime.Today.Year;

    protected override void OnParametersSet()
    {
        if (Value is not null)
        {
            angezeigtesJahr = Value.Value.Year;
        }
    }

    private void WechsleJahr(int delta) => angezeigtesJahr += delta;

    private async Task WaehleAsync(int monat)
    {
        var wert = new DateTime(angezeigtesJahr, monat, 1);
        Value = wert;
        await ValueChanged.InvokeAsync(wert);
    }

    private static string Monatsname(int monat)
        => new DateTime(2000, monat, 1).ToString("MMM", new System.Globalization.CultureInfo("fr-FR"));
}
