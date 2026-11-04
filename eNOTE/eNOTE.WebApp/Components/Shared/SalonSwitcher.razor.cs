using ENote.WebApp.Components.Shared;
using Microsoft.AspNetCore.Components;
using System.Security.Claims;

namespace ENote.WebApp.Components.Shared;

public partial class SalonSwitcher : IDisposable
{
    private List<EnoteSelectOption<string>> optionen = [];
    private string aktiveId = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        AktiverSalon.OnChange += Aktualisiere;

        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return;
        }

        if (!AktiverSalon.IstGeladen)
        {
            await AktiverSalon.InitialisiereAsync(userId);
        }

        Aktualisiere();
    }

    private void Aktualisiere()
    {
        optionen = AktiverSalon.Laeden
            .Select(l => new EnoteSelectOption<string> { Value = l.SalonId.ToString(), Label = l.Salon.Name })
            .ToList();
        aktiveId = AktiverSalon.Aktiv?.SalonId.ToString() ?? string.Empty;
        _ = InvokeAsync(StateHasChanged);
    }

    private async Task WaehleAsync(string id)
    {
        if (Guid.TryParse(id, out var salonId))
        {
            await AktiverSalon.WaehleAsync(salonId);
        }
    }

    public void Dispose() => AktiverSalon.OnChange -= Aktualisiere;
}
