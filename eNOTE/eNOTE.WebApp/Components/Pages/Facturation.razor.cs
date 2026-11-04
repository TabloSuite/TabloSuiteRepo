using ENote.Core.DataModels.Abo;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;

namespace ENote.WebApp.Components.Pages;

public partial class Facturation(AboService aboService, ToastService toast)
{
    private List<NutzungsAbo> gemeldete = [];
    private List<NutzungsAbo> alle = [];
    private bool isLoading = true;

    protected override async Task OnInitializedAsync() => await LadeAsync();

    private async Task LadeAsync()
    {
        isLoading = true;
        gemeldete = await aboService.GetGemeldeteZahlungenAsync();
        alle = await aboService.GetAlleAbosAsync();
        isLoading = false;
    }

    private async Task SetzeStatusAsync(Guid aboId, AboZahlungStatus status)
    {
        await aboService.SetzeZahlungsStatusAsync(aboId, status);
        toast.ZeigeErfolg(status == AboZahlungStatus.Bestaetigt ? "Paiement confirmé." : "Paiement refusé.");
        await LadeAsync();
    }

    private static string IntervallText(AboIntervall intervall) => intervall switch
    {
        AboIntervall.Monatlich => "Mensuel",
        AboIntervall.DreiMonate => "3 mois",
        AboIntervall.Jaehrlich => "Annuel",
        _ => intervall.ToString()
    };

    private static string StatusText(AboZahlungStatus status) => status switch
    {
        AboZahlungStatus.Offen => "À payer",
        AboZahlungStatus.Gemeldet => "Signalé",
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
