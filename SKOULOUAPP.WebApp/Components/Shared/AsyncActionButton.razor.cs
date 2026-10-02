using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace SkoulouApp.WebApp.Components.Shared;

public partial class AsyncActionButton
{
    [Parameter] public string Type { get; set; } = "button";
    [Parameter] public string Class { get; set; } = string.Empty;
    [Parameter] public bool IsBusy { get; set; }
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public string BusyLabel { get; set; } = "Traitement...";
    [Parameter] public EventCallback<MouseEventArgs> OnClick { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private string ButtonClass => string.IsNullOrWhiteSpace(Class)
        ? "inline-flex items-center justify-center gap-2 disabled:cursor-wait disabled:opacity-70"
        : $"inline-flex items-center justify-center gap-2 disabled:cursor-wait disabled:opacity-70 {Class}";
}
