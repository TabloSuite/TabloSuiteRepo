using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace HalloApp.WebApp.Components.Shared;

public partial class RichTextEditor : ComponentBase, IAsyncDisposable
{
    private ElementReference editorElement;
    private DotNetObjectReference<RichTextEditor>? dotNetRef;
    private bool initialisiert;
    private string? letzterGesetzterWert;
    private bool modalGeoeffnet;
    private string modalEntwurf = string.Empty;
    private RichTextEditor? modalEditor;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    [Parameter]
    public string? Value { get; set; }

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    [Parameter]
    public string Placeholder { get; set; } = string.Empty;

    [Parameter]
    public bool ReadOnly { get; set; }

    /// <summary>Aktiviert das Einfügen von Bildern (Toolbar-Button + Copy/Paste).</summary>
    [Parameter]
    public bool EnableImages { get; set; }

    /// <summary>Ermöglicht einen externen, verwalteten Bilddialog zusätzlich zum Quill-Dateidialog.</summary>
    [Parameter]
    public EventCallback RequestImageLibrary { get; set; }

    /// <summary>Informiert den aufrufenden Editor, dass dieser Editor Ziel für das nächste Bibliotheksbild ist.</summary>
    [Parameter]
    public EventCallback<RichTextEditor> ImageLibraryTargetRequested { get; set; }

    /// <summary>Erlaubt das Bearbeiten des Inhalts in einem großen Modal.</summary>
    [Parameter]
    public bool EnableLargeEditor { get; set; } = true;

    /// <summary>
    /// Aktiviert den Word-ähnlichen Seitenmodus: EIN Editor mit visueller
    /// A4-Seitendarstellung. Der Inhalt wird erst beim Speichern aufgeteilt.
    /// </summary>
    [Parameter]
    public bool Paged { get; set; }

    /// <summary>Wird mit der aktuellen A4-Seitenanzahl im Paged-Modus aufgerufen.</summary>
    [Parameter]
    public EventCallback<int> OnPageCount { get; set; }

    /// <summary>Standard-Schriftart
    [Parameter]
    public string? DefaultFont { get; set; } = "arial";

    /// <summary>Standard-Schriftgröße für neue/leere Inhalte.</summary>
    [Parameter]
    public string? DefaultSize { get; set; } = "20px";

    /// <summary>Optionale Mindesthöhe des Editor-Inhaltsbereichs in rem.</summary>
    [Parameter]
    public double? MinEditorHeightRem { get; set; }

    private string? ContainerStyle => MinEditorHeightRem is > 0
        ? $"--hallo-richtext-min-height:{MinEditorHeightRem:0.##}rem;"
        : null;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            dotNetRef = DotNetObjectReference.Create(this);
            await JSRuntime.InvokeVoidAsync(
                "SKOULOUAPPRichText.init",
                editorElement,
                dotNetRef,
                Value ?? string.Empty,
                Placeholder,
                ReadOnly,
                EnableImages,
                0,
                DefaultFont,
                DefaultSize,
                Paged);

            letzterGesetzterWert = Value;
            initialisiert = true;
            return;
        }

        if (initialisiert && Value != letzterGesetzterWert)
        {
            letzterGesetzterWert = Value;
            await JSRuntime.InvokeVoidAsync("SKOULOUAPPRichText.setHtml", editorElement, Value ?? string.Empty);
        }
    }

    [JSInvokable]
    public async Task OnContentChanged(string html)
    {
        letzterGesetzterWert = html;
        Value = html;
        await ValueChanged.InvokeAsync(html);
    }

    [JSInvokable]
    public async Task OnPageCountChanged(int pageCount)
    {
        if (OnPageCount.HasDelegate)
        {
            await OnPageCount.InvokeAsync(pageCount);
        }
    }

    /// <summary>Fügt eine bereits autorisierte Bild-URL an der Cursorposition ein.</summary>
    public async Task InsertImageAsync(string imageUrl, string altText)
    {
        if (!initialisiert)
        {
            return;
        }

        await JSRuntime.InvokeVoidAsync("SKOULOUAPPRichText.insertImageUrl", editorElement, imageUrl, altText);
    }

    [JSInvokable]
    public async Task OpenImageLibraryAsync()
    {
        if (ImageLibraryTargetRequested.HasDelegate)
        {
            await ImageLibraryTargetRequested.InvokeAsync(this);
        }

        if (RequestImageLibrary.HasDelegate)
        {
            await RequestImageLibrary.InvokeAsync();
        }
    }

    /// <summary>
    /// Teilt den aktuellen Inhalt an den gemessenen A4-Grenzen in einzelne
    /// Seiten-HTML-Strings auf (für Speichern und PDF-Export).
    /// </summary>
    public async Task<List<string>> SplitIntoPagesAsync()
    {
        if (!initialisiert)
        {
            return [];
        }

        var pages = await JSRuntime.InvokeAsync<List<string>>("SKOULOUAPPRichText.splitIntoPages", editorElement);
        return pages ?? [];
    }

    /// <summary>Liest den tatsächlich im Quill-Editor sichtbaren HTML-Inhalt.</summary>
    public async Task<string> GetHtmlAsync()
    {
        if (!initialisiert)
        {
            return Value ?? string.Empty;
        }

        return await JSRuntime.InvokeAsync<string>("SKOULOUAPPRichText.getHtml", editorElement);
    }

    private async Task OpenLargeEditorAsync()
    {
        // Der gebundene Wert kann während eines noch nicht abgeschlossenen
        // Blazor-Renders hinter Quill zurückliegen. Für den Modalentwurf immer
        // den tatsächlich sichtbaren Inline-Inhalt verwenden.
        modalEntwurf = await GetHtmlAsync();
        Value = modalEntwurf;
        letzterGesetzterWert = modalEntwurf;
        modalGeoeffnet = true;
    }

    private void CancelLargeEditor()
    {
        modalGeoeffnet = false;
        modalEntwurf = string.Empty;
        modalEditor = null;
    }

    private async Task ApplyLargeEditorAsync()
    {
        // Auch beim Übernehmen den aktuellen Quill-Inhalt lesen, damit eine
        // unmittelbar vor dem Klick erfolgte Eingabe nicht verloren geht.
        var html = modalEditor is null ? modalEntwurf : await modalEditor.GetHtmlAsync();
        modalGeoeffnet = false;
        modalEditor = null;
        modalEntwurf = html;
        Value = html;
        letzterGesetzterWert = html;

        // setHtml löst absichtlich kein text-change-Ereignis aus. Deshalb den
        // Inline-Editor direkt aktualisieren und anschließend den gebundenen
        // Wert an die aufrufende Komponente zurückmelden.
        if (initialisiert)
        {
            await JSRuntime.InvokeVoidAsync("SKOULOUAPPRichText.setHtml", editorElement, html);
        }

        await ValueChanged.InvokeAsync(html);
    }

    public async ValueTask DisposeAsync()
    {
        if (initialisiert)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("SKOULOUAPPRichText.destroy", editorElement);
            }
            catch (JSDisconnectedException)
            {
                // Circuit bereits getrennt, kein Aufräumen nötig.
            }
        }

        dotNetRef?.Dispose();
    }
}
