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

    /// <summary>Event für Zwei-Wege-Bindung (VorschauUrl wird nach Upload gesetzt).</summary>
    [Parameter] public EventCallback<string?> VorschauUrlChanged { get; set; }

    // Internes Feld für die Vorschau (wird nach Upload gesetzt, rendert neu).
    private string? interneVorschau;
    private string? AnzeigeVorschau => interneVorschau ?? VorschauUrl;

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
            Fehler = "Veuillez choisir une image (JPG, PNG, GIF, WebP).";
            return;
        }

        // Größenprüfung vor dem Lesen (vermeidet Exception bei >8 MB).
        if (datei.Size > 8 * 1024 * 1024)
        {
            Fehler = "Le fichier dépasse la limite de 8 Mo.";
            return;
        }

        try
        {
            // Als Data-URL lesen.
            await using var stream = datei.OpenReadStream(8 * 1024 * 1024);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var dataUrl = $"data:{datei.ContentType};base64,{Convert.ToBase64String(bytes)}";

            // Vorschau sofort anzeigen (Original), damit der Nutzer sieht, dass die Auswahl funktioniert.
            interneVorschau = dataUrl;
            StateHasChanged();

            // Danach quadratisch zuschneiden (kann fehlschlagen, dann bleibt das Original als Vorschau).
            string vorschau = dataUrl;
            byte[] bildBytes = bytes;
            string contentType = datei.ContentType;
            try
            {
                vorschau = await Js.InvokeAsync<string>("enoteSchneideBildZu", dataUrl, ZielGroesse);
                var basis64 = vorschau[(vorschau.IndexOf(',') + 1)..];
                bildBytes = Convert.FromBase64String(basis64);
                contentType = "image/jpeg";
                interneVorschau = vorschau;
                StateHasChanged();
            }
            catch (Exception jsEx)
            {
                // Zuschnitt fehlgeschlagen: Original als Vorschau behalten, aber den Upload nicht blockieren.
                System.Diagnostics.Debug.WriteLine($"Bild-Zuschnitt fehlgeschlagen: {jsEx.Message}");
            }

            await VorschauUrlChanged.InvokeAsync(vorschau);
            var dateiName = Path.GetFileNameWithoutExtension(datei.Name) + (contentType == "image/jpeg" ? ".jpg" : Path.GetExtension(datei.Name));
            await OnBildBereit.InvokeAsync((bildBytes, contentType, dateiName));
        }
        catch (Exception ex)
        {
            // Konkrete Fehlermeldung je nach Ursache (Größe, Format, Browser).
            Fehler = ex switch
            {
                IOException => "Le fichier est trop volumineux ou illisible.",
                JSException => "Le format de l'image n'est pas pris en charge. Essayez JPG ou PNG.",
                _ => $"Erreur: {ex.GetType().Name} – {ex.Message}" // Temporär: konkrete Fehler für Debugging
            };
            StateHasChanged(); // Fehler sofort anzeigen
        }
    }

    private async Task Entfernen()
    {
        interneVorschau = null;
        Fehler = null;
        await VorschauUrlChanged.InvokeAsync(null); // Aufrufer über Entfernen informieren
        StateHasChanged(); // Neu rendern
        await OnEntfernt.InvokeAsync();
    }
}
