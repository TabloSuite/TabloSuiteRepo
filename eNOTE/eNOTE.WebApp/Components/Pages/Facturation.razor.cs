using ENote.Core.DataModels.Abo;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Facturation(
    AboService aboService,
    VertragsService vertragsService,
    ToastService toast,
    LadeZustand ladeZustand,
    AuthenticationStateProvider authStateProvider)
{
    private Guid benutzerId;

    /// <summary>Nur der SuperAdmin bestätigt Zahlungseingänge (ProduktOwner sehen ihre Läden nur).</summary>
    private bool darfBestaetigen;
    private List<NutzungsAbo> gemeldete = [];
    private List<NutzungsAbo> alle = [];
    private bool isLoading = true;

    // Suche/Filter
    private string aboSuche = string.Empty;
    private string aboStatusFilter = "alle";

    private static readonly IReadOnlyList<ENote.WebApp.Components.Shared.EnoteSelectOption<string>> aboStatusOptionen =
    [
        new() { Value = "alle", Label = "Tous les statuts" },
        new() { Value = "Offen", Label = "Ouvert" },
        new() { Value = "Gemeldet", Label = "Signalé" },
        new() { Value = "Bestaetigt", Label = "Confirmé" },
        new() { Value = "Abgelehnt", Label = "Refusé" }
    ];

    // Suche und Status laufen in der Datenbank, geladen wird seitenweise; Kennzahlen über ALLE Abos.
    private IEnumerable<NutzungsAbo> GefilterteAbos() => alle;
    private bool alleMehr;
    private AboService.FacturationKennzahlen kennzahlen = new(0, 0, 0);
    private CancellationTokenSource? suchVerzoegerung;
    private static DateTime MonatsAnfangUtc => new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private AboZahlungStatus? GewaehlterStatus
        => aboStatusFilter != "alle" && Enum.TryParse<AboZahlungStatus>(aboStatusFilter, out var status) ? status : null;

    private async Task LadeAbosAsync()
        => (alle, alleMehr) = await aboService.SucheAbosAsync(benutzerId, aboSuche, GewaehlterStatus, 0);

    private async Task MehrAbosAsync()
    {
        var (neu, mehr) = await aboService.SucheAbosAsync(benutzerId, aboSuche, GewaehlterStatus, alle.Count);
        alle.AddRange(neu);
        alleMehr = mehr;
    }

    private async Task SucheVerzoegertAsync()
    {
        suchVerzoegerung?.Cancel();
        var cts = suchVerzoegerung = new CancellationTokenSource();
        try { await Task.Delay(350, cts.Token); }
        catch (TaskCanceledException) { return; }
        await LadeAbosAsync();
    }

    protected override async Task OnInitializedAsync()
    {
        using var _ = ladeZustand.Beginnen();
        var auth = await authStateProvider.GetAuthenticationStateAsync();
        Guid.TryParse(auth.User.FindFirstValue(ClaimTypes.NameIdentifier), out benutzerId);
        darfBestaetigen = auth.User.IsInRole(ENote.Core.DataModels.Identity.AppRollen.SuperAdmin);
        await LadeAsync();
    }

    private async Task LadeAsync()
    {
        isLoading = true;
        // Fällige Perioden aller Läden erzeugen, damit die Übersicht aktuell ist.
        await vertragsService.SynchronisiereAlleAsync();
        gemeldete = await aboService.GetGemeldeteZahlungenAsync(benutzerId);
        kennzahlen = await aboService.GetKennzahlenAsync(benutzerId, MonatsAnfangUtc);
        await LadeAbosAsync();
        isLoading = false;
    }

    private async Task SetzeStatusAsync(Guid aboId, AboZahlungStatus status)
    {
        try
        {
            await aboService.SetzeZahlungsStatusAsync(benutzerId, aboId, status);
        }
        catch (UnauthorizedAccessException ex)
        {
            toast.ZeigeFehler(ex.Message);
            return;
        }

        toast.ZeigeErfolg(status == AboZahlungStatus.Bestaetigt ? "Paiement confirmé." : "Paiement refusé.");
        await LadeAsync();
    }

    private static string IntervallText(AboIntervall intervall) => intervall switch
    {
        AboIntervall.Monatlich => "Mensuel",
        AboIntervall.DreiMonate => "Trimestriel",
        AboIntervall.SechsMonate => "Semestriel",
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
