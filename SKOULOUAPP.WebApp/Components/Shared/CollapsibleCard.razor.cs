using Microsoft.AspNetCore.Components;

namespace HalloApp.WebApp.Components.Shared;

public partial class CollapsibleCard
{
    private bool internalExpanded;
    private bool initialized;

    [Parameter] public string Title { get; set; } = string.Empty;
    [Parameter] public string? Subtitle { get; set; }
    [Parameter] public string? Eyebrow { get; set; }
    [Parameter] public string? Icon { get; set; }
    [Parameter] public string Accent { get; set; } = "slate";
    /// <summary>Karten starten standardmäßig geschlossen; Seiten können dies bei Bedarf explizit überschreiben.</summary>
    [Parameter] public bool InitiallyExpanded { get; set; }
    [Parameter] public RenderFragment? HeaderRight { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    // Externe Steuerung (z. B. "Alle auf-/zuklappen"). Wenn gesetzt, überschreibt den internen Zustand.
    [Parameter] public bool? Expanded { get; set; }
    [Parameter] public EventCallback<bool> ExpandedChanged { get; set; }

    private bool IsExpanded => Expanded ?? internalExpanded;

    protected override void OnInitialized()
    {
        if (!initialized)
        {
            internalExpanded = InitiallyExpanded;
            initialized = true;
        }
    }

    private async Task ToggleAsync()
    {
        var next = !IsExpanded;
        internalExpanded = next;
        if (ExpandedChanged.HasDelegate)
        {
            await ExpandedChanged.InvokeAsync(next);
        }
    }

    private string EyebrowClass => Accent switch
    {
        "blue" => "text-blue-700",
        "indigo" => "text-indigo-700",
        "emerald" => "text-emerald-700",
        "amber" => "text-amber-700",
        "rose" => "text-rose-700",
        "cyan" => "text-cyan-700",
        "violet" => "text-violet-700",
        _ => "text-slate-600"
    };

    private string IconWrapperClass => Accent switch
    {
        "blue" => "bg-blue-50 text-blue-700",
        "indigo" => "bg-indigo-50 text-indigo-700",
        "emerald" => "bg-emerald-50 text-emerald-700",
        "amber" => "bg-amber-50 text-amber-700",
        "rose" => "bg-rose-50 text-rose-700",
        "cyan" => "bg-cyan-50 text-cyan-700",
        "violet" => "bg-violet-50 text-violet-700",
        _ => "bg-slate-100 text-slate-700"
    };
}
