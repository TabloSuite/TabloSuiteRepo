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

    /// <summary>
    /// Delegiertes Recht, das diese Seite öffnet: "finanzen", "leistungen" oder "rabatte".
    /// Ein Mitarbeiter mit eigenem Konto und diesem Recht braucht keine Besitzer-PIN.
    /// </summary>
    [Parameter] public string? Recht { get; set; }

    private string pin = string.Empty;
    private string? fehler;
    private Guid salonId;
    private bool laedt = true;

    // Sperre wird bei jedem Rendern neu ausgewertet: verriegelt der Besitzer die Zone (Seitenleiste),
    // verschwindet die gerade offene Verwaltungsseite sofort hinter der PIN-Abfrage.
    private bool ohneKonto;
    private bool ausgenommen;
    private bool hatBesitzerPin;
    private bool istGesperrt => ohneKonto || (!ausgenommen && hatBesitzerPin && !PinState.VerwaltungFreigeschaltet);

    protected override async Task OnInitializedAsync()
    {
        PinState.OnChange += BeiPinWechsel;

        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            ohneKonto = true;
            laedt = false;
            return;
        }

        // SuperAdmin und ProduktOwner verwalten fremde Läden und werden nicht per
        // Besitzer-PIN gesperrt. Für den Laden selbst (Besitzer/Kassierer am gemeinsamen
        // Tablet) gilt die Sperre dagegen immer, sobald eine Besitzer-PIN konfiguriert ist:
        // Auch der angemeldete Besitzer sperrt seine Zone, damit kein Kassierer in der
        // offenen Sitzung Verwaltungsseiten öffnet.
        var istPlattformVerwalter =
            authState.User.IsInRole(Core.DataModels.Identity.AppRollen.SuperAdmin)
            || authState.User.IsInRole(Core.DataModels.Identity.AppRollen.ProduktOwner);

        if (istPlattformVerwalter)
        {
            ausgenommen = true;
            laedt = false;
            return;
        }

        var zuordnung = await KatalogService.GetUserSalonAsync(userId);
        salonId = zuordnung?.SalonId ?? Guid.Empty;

        // Sperre ist der Standard, sobald eine Besitzer-PIN konfiguriert ist
        // (damit der Besitzer das Ausschalten nicht vergisst). Erst die korrekte
        // PIN schaltet frei; beim Kassenabschluss wird automatisch wieder gesperrt.
        if (zuordnung is not null && HatRecht(zuordnung, Recht)
            && authState.User.IsInRole(Core.DataModels.Identity.AppRollen.Kassierer)
            && !authState.User.IsInRole(Core.DataModels.Identity.AppRollen.Besitzer))
        {
            ausgenommen = true;
            laedt = false;
            return;
        }

        hatBesitzerPin = salonId != Guid.Empty && await KassenService.HatBesitzerPinAsync(salonId);
        laedt = false;
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

    private static bool HatRecht(Core.DataModels.Salons.UserSalon zuordnung, string? recht) => recht switch
    {
        "finanzen" => zuordnung.KannFinanzenSehen,
        "leistungen" => zuordnung.KannLeistungenVerwalten,
        "rabatte" => zuordnung.KannRabatteVerwalten,
        _ => false
    };

    private void BeiPinWechsel() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => PinState.OnChange -= BeiPinWechsel;
}
