using Microsoft.AspNetCore.Components;

namespace HalloApp.WebApp.Components.Shared;

public partial class PasswordInput : ComponentBase
{
    [Parameter]
    public string? Value { get; set; }

    [Parameter]
    public EventCallback<string?> ValueChanged { get; set; }

    [Parameter]
    public string Autocomplete { get; set; } = "current-password";

    [Parameter]
    public string? Name { get; set; }

    [Parameter]
    public bool Required { get; set; }

    [Parameter]
    public string InputClass { get; set; } = "hallo-input pr-12";

    private bool isVisible;

    private Task OnValueChangedAsync(ChangeEventArgs args)
        => ValueChanged.InvokeAsync(args.Value?.ToString());

    private void ToggleVisibility() => isVisible = !isVisible;
}