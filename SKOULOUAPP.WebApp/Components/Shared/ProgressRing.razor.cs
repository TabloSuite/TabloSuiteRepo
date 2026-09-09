using Microsoft.AspNetCore.Components;

namespace HalloApp.WebApp.Components.Shared;

public partial class ProgressRing
{
    [Parameter] public string Size { get; set; } = "1rem";
    [Parameter] public string Label { get; set; } = "Chargement";
    [Parameter] public string? Class { get; set; }
}
