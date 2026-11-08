using ENote.Core.DataModels.Abo;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Abonnement(
    AboService aboService,
    KatalogService katalogService,
    AktiverSalonState aktiverSalon,
    ToastService toast,
    AuthenticationStateProvider authStateProvider,
    LadeZustand ladeZustand) : IDisposable
{
    private readonly AboIntervall[] formeln = [AboIntervall.Monatlich, AboIntervall.DreiMonate, AboIntervall.Jaehrlich];

    private bool isLoading = true;
    private Guid salonId;
    private Guid benutzerId;
    private string salonName = string.Empty;
    private NutzungsAbo? aktuellesAbo;
    private Dictionary<AboIntervall, decimal> preise = [];
    private bool probeLaeuft;
    private DateTime? probeBis;

    private bool zahlungOffen;
    private AboIntervall ausgewaehltesIntervall;
    private string? zahlungsNotiz;
    private string? zahlungFehler;

    protected override async Task OnInitializedAsync()
    {
        using var _ = ladeZustand.Beginnen();
        aktiverSalon.OnChange += HandleSalonWechsel;

        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            isLoading = false;
            return;
        }

        benutzerId = userId;
        if (!aktiverSalon.IstGeladen)
        {
            await aktiverSalon.InitialisiereAsync(userId);
        }

        var aktive = aktiverSalon.Aktiv;
        if (aktive is null)
        {
            isLoading = false;
            return;
        }

        salonId = aktive.SalonId;
        salonName = aktive.Salon.Name;
        await LadeAsync();
    }

    private void HandleSalonWechsel()
    {
        var aktive = aktiverSalon.Aktiv;
        if (aktive is null || aktive.SalonId == salonId)
        {
            return;
        }

        salonId = aktive.SalonId;
        salonName = aktive.Salon.Name;
        _ = InvokeAsync(async () => { await LadeAsync(); StateHasChanged(); });
    }

    public void Dispose() => aktiverSalon.OnChange -= HandleSalonWechsel;

    private async Task LadeAsync()
    {
        isLoading = true;
        aktuellesAbo = await aboService.GetAktuellesAboAsync(salonId);
        (probeLaeuft, probeBis) = await aboService.GetProbeStatusAsync(salonId);
        preise = new Dictionary<AboIntervall, decimal>();
        foreach (var formel in formeln)
        {
            preise[formel] = await aboService.GetPreisAsync(salonId, formel);
        }

        isLoading = false;
    }

    private void OeffneZahlung(AboIntervall intervall)
    {
        ausgewaehltesIntervall = intervall;
        zahlungsNotiz = null;
        zahlungFehler = null;
        zahlungOffen = true;
    }

    private async Task MeldeZahlungAsync()
    {
        var (erfolg, fehler, _) = await aboService.MeldeZahlungAsync(salonId, ausgewaehltesIntervall, zahlungsNotiz);
        if (!erfolg)
        {
            zahlungFehler = fehler;
            return;
        }

        zahlungOffen = false;
        toast.ZeigeErfolg("Paiement signalé. L'administrateur confirmera la réception.");
        await LadeAsync();
    }

    private static string IntervallText(AboIntervall intervall) => intervall switch
    {
        AboIntervall.Monatlich => "Mensuel",
        AboIntervall.DreiMonate => "3 mois",
        AboIntervall.Jaehrlich => "Annuel",
        _ => intervall.ToString()
    };

    private string ZahlungDialogTitel => "Signaler le paiement – " + IntervallText(ausgewaehltesIntervall);

    private static string StatusText(AboZahlungStatus status) => status switch
    {
        AboZahlungStatus.Offen => "À payer",
        AboZahlungStatus.Gemeldet => "Paiement signalé",
        AboZahlungStatus.Bestaetigt => "Confirmé",
        AboZahlungStatus.Abgelehnt => "Refusé",
        _ => status.ToString()
    };

    private static string StatusKlasse(AboZahlungStatus status) => status switch
    {
        AboZahlungStatus.Bestaetigt => "",
        AboZahlungStatus.Gemeldet => "enote-tag-warn",
        AboZahlungStatus.Abgelehnt => "enote-tag-off",
        _ => "enote-tag-warn"
    };
}
