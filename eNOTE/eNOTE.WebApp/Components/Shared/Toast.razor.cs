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

    protected override void OnInitialized() => ToastService.OnChange += StateHasChanged;

    private void Verbergen() => ToastService.Verbergen();

    public void Dispose() => ToastService.OnChange -= StateHasChanged;
}
