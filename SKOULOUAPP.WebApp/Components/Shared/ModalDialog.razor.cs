using Microsoft.AspNetCore.Components;

namespace SkoulouApp.WebApp.Components.Shared;

public partial class ModalDialog
{
    [Parameter]
    public bool Open { get; set; }

    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    [Parameter]
    public string? Title { get; set; }

    [Parameter]
    public string? Eyebrow { get; set; }

    [Parameter]
    public string? ErrorMessage { get; set; }

    [Parameter]
    public string MaxWidthClass { get; set; } = "max-w-3xl";

    [Parameter]
    public bool CloseOnBackdrop { get; set; } = true;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public bool ScrollBody { get; set; } = true;

    [Parameter]
    public string ZIndexClass { get; set; } = "z-[2200]";

    private async Task CloseAsync()
    {
        if (OpenChanged.HasDelegate)
        {
            await OpenChanged.InvokeAsync(false);
        }
    }

    private async Task OnBackdropClickAsync()
    {
        if (CloseOnBackdrop)
        {
            await CloseAsync();
        }
    }
}
