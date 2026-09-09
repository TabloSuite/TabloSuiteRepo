using Microsoft.AspNetCore.Components;

namespace HalloApp.WebApp.Components.Shared;

public partial class ProvisionKarte
{
    [Parameter, EditorRequired]
    public string Titel { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public decimal Betrag { get; set; }

    [Parameter]
    public string Farbe { get; set; } = "from-slate-500 to-slate-700";
}
