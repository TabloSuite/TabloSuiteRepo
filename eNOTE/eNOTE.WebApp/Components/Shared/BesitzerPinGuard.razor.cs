using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using System.Security.Claims;

namespace ENote.WebApp.Components.Shared;

/// <summary>
/// Sperrt einen Verwaltungsbereich, bis die korrekte Besitzer-PIN eingegeben wurde.
/// Nicht angemeldete Besitzer/Admins (die sich „normal“ angemeldet haben) sind nie gesperrt.
/// </summary>
public partial class BesitzerPinGuard : IDisposable
{
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private string pin = string.Empty;
    private string? fehler;
    private Guid salonId;
    private bool istGesperrt = true;

    protected override async Task OnInitializedAsync()
    {
        PinState.OnChange += StateHasChanged;

        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            istGesperrt = true;
            return;
        }

        var zuordnung = await KatalogService.GetUserSalonAsync(userId);
        salonId = zuordnung?.SalonId ?? Guid.Empty;

        // Sperre ist der Standard, sobald eine Besitzer-PIN konfiguriert ist
        // (damit der Besitzer das Ausschalten nicht vergisst). Erst die korrekte
        // PIN schaltet frei; beim Kassenabschluss wird automatisch wieder gesperrt.
        var hatBesitzerPin = salonId != Guid.Empty && await KassenService.HatBesitzerPinAsync(salonId);
        istGesperrt = hatBesitzerPin && !PinState.VerwaltungFreigeschaltet;
    }

    private async Task PruefeAsync()
    {
        fehler = null;

        if (string.IsNullOrWhiteSpace(pin))
        {
            fehler = "Veuillez saisir le code PIN.";
            return;
        }

        var (erfolg, fehlerText) = await KassenService.PruefeBesitzerPinAsync(salonId, pin);
        if (erfolg)
        {
            pin = string.Empty;
            PinState.Freischalten();
            Toast.ZeigeErfolg("Zone déverrouillée.");
        }
        else
        {
            fehler = fehlerText;
            pin = string.Empty;
        }
    }

    private async Task HandleTaste(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await PruefeAsync();
        }
    }

    public void Dispose() => PinState.OnChange -= StateHasChanged;
}
