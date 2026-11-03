using Microsoft.AspNetCore.Components;

namespace ENote.WebApp.Components.Shared;

public partial class CollapsibleCard
{
    [Parameter] public string Title { get; set; } = string.Empty;
    [Parameter] public string? Subtitle { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Bereiche starten standardmäßig geschlossen (Projektvorgabe).</summary>
    [Parameter] public bool IsExpanded { get; set; }

    private void Toggle() => IsExpanded = !IsExpanded;
}
