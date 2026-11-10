using ENote.WebApp.Services;

namespace ENote.WebApp.Components.Shared;

public partial class Toast : IDisposable
{
    private string ArtKlasse => ToastService.Art switch
    {
        ToastArt.Erfolg => "erfolg",
        ToastArt.Fehler => "fehler",
        _ => "info"
    };

    // Das automatische Ausblenden kommt von einem Timer-Thread – daher über den Renderer-Kontext.
    private void HandleChange() => _ = InvokeAsync(StateHasChanged);

    protected override void OnInitialized() => ToastService.OnChange += HandleChange;

    private void Verbergen() => ToastService.Verbergen();

    public void Dispose() => ToastService.OnChange -= HandleChange;
}
