using Microsoft.AspNetCore.Components;

namespace ENote.WebApp.Components.Shared;

public partial class ModalDialog
{
    [Parameter] public bool Open { get; set; }
    [Parameter] public string Title { get; set; } = string.Empty;
    [Parameter] public string? ErrorMessage { get; set; }
    [Parameter] public string MaxWidthClass { get; set; } = "enote-modal-md";
    [Parameter] public bool CloseOnBackdropClick { get; set; } = true;
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public RenderFragment? Footer { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private Task CloseAsync() => OnClose.InvokeAsync();

    private Task OnBackdropClickAsync()
        => CloseOnBackdropClick ? CloseAsync() : Task.CompletedTask;
}
