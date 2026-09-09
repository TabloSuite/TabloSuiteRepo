using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace HalloApp.WebApp.Components.Layout;

public partial class ScrollToTopButton : IDisposable
{
    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JS.InvokeVoidAsync("SKOULOUAPPScrollToTop.init");
        }
    }

    public void Dispose()
    {
    }
}
