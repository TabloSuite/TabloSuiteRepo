using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace ENote.WebApp.Components.Shared;

/// <summary>
/// Wiederverwendbare Bild-Auswahl: Foto direkt mit der Kamera aufnehmen (Handy/Tablet)
/// oder eine Datei wählen. Schneidet das Bild quadratisch zu (Mitte) und liefert es
/// als Bytes + Data-URL-Vorschau an den Aufrufer.
/// </summary>
public partial class BildAuswahl
{
    /// <summary>Aufgerufen, wenn ein zugeschnittenes Bild bereit ist (JPEG-Bytes + ContentType + Dateiname).</summary>
    [Parameter] public EventCallback<(byte[] Bytes, string ContentType, string DateiName)> OnBildBereit { get; set; }

    /// <summary>Aufgerufen, wenn das Bild entfernt wird.</summary>
    [Parameter] public EventCallback OnEntfernt { get; set; }

    /// <summary>Aktuelle Vorschau-URL (vom Aufrufer gesetzt, z. B. vorhandenes Bild).</summary>
    [Parameter] public string? VorschauUrl { get; set; }

    /// <summary>Maximale Kantenlänge des quadratischen Zuschnitts (px).</summary>
    [Parameter] public int ZielGroesse { get; set; } = 800;

    [Inject] private IJSRuntime Js { get; set; } = default!;

    private string? Fehler;

    private Task HandleKamera(InputFileChangeEventArgs e) => VerarbeiteAsync(e.File);
    private Task HandleDatei(InputFileChangeEventArgs e) => VerarbeiteAsync(e.File);

    private async Task VerarbeiteAsync(IBrowserFile? datei)
    {
        Fehler = null;
        if (datei is null)
        {
            return;
        }

        if (!datei.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            Fehler = "Veuillez choisir une image.";
            return;
        }

        try
        {
            // Als Data-URL lesen, quadratisch zuschneiden, zurück in Bytes.
            await using var stream = datei.OpenReadStream(8 * 1024 * 1024);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var dataUrl = $"data:{datei.ContentType};base64,{Convert.ToBase64String(ms.ToArray())}";

            var zugeschnitten = await Js.InvokeAsync<string>("enoteSchneideBildZu", dataUrl, ZielGroesse);
            var basis64 = zugeschnitten[(zugeschnitten.IndexOf(',') + 1)..];
            var bytes = Convert.FromBase64String(basis64);

            VorschauUrl = zugeschnitten;
            var dateiName = Path.GetFileNameWithoutExtension(datei.Name) + ".jpg";
            await OnBildBereit.InvokeAsync((bytes, "image/jpeg", dateiName));
        }
        catch (Exception)
        {
            Fehler = "Le traitement de l'image a échoué.";
        }
    }

    private async Task Entfernen()
    {
        VorschauUrl = null;
        Fehler = null;
        await OnEntfernt.InvokeAsync();
    }
}
