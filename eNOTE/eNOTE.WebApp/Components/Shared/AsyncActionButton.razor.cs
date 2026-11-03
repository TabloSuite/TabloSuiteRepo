using Microsoft.AspNetCore.Components;

namespace ENote.WebApp.Components.Shared;

public partial class AsyncActionButton
{
    [Parameter] public string Text { get; set; } = "Enregistrer";
    [Parameter] public string? BusyText { get; set; }
    [Parameter] public string Type { get; set; } = "button";
    [Parameter] public string? CssClass { get; set; }
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }

    private bool IsBusy { get; set; }

    private async Task HandleClickAsync()
    {
        if (IsBusy || !OnClick.HasDelegate)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await OnClick.InvokeAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
