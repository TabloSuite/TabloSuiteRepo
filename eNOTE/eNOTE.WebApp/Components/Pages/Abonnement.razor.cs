using ENote.Core.DataModels.Abo;
using ENote.Core.Services;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

/// <summary>
/// Abonnement des Besitzers (automatischer Vertrag): Perioden entstehen automatisch nach der
/// Probezeit; der Besitzer wählt die Formel für die nächste Periode und meldet Zahlungen,
/// der Administrator bestätigt den Eingang.
/// </summary>
public partial class Abonnement(
    AboService aboService,
    VertragsService vertragsService,
    AktiverSalonState aktiverSalon,
    ToastService toast,
    AuthenticationStateProvider authStateProvider,
    LadeZustand ladeZustand,
    ILogger<Abonnement> logger) : IDisposable
{
    private List<AboService.FormelPreis> formeln = [];
    private VertragsService.LadenVertrag? vertrag;

    private bool isLoading = true;
    private string? ladeFehler;
    private Guid salonId;
    private Guid benutzerId;
    private string salonName = string.Empty;

    // Formelwechsel
    private AboIntervall? wechselZiel;
    private VertragsService.WechselVorschau? wechselVorschau;

    // Zahlungsmeldung
    private VertragsService.ZahlungEintrag? meldungFuer;
    private string? zahlungsNotiz;
    private string? zahlungFehler;

    private IEnumerable<VertragsService.ZahlungEintrag> OffenePerioden =>
        vertrag?.Perioden.Where(p => p.Status is AboZahlungStatus.Offen or AboZahlungStatus.Abgelehnt) ?? [];

    protected override async Task OnInitializedAsync()
    {
        using var _ = ladeZustand.Beginnen();
        aktiverSalon.OnChange += HandleSalonWechsel;

        var authState = await authStateProvider.GetAuthenticationStateAsync();
        if (!Guid.TryParse(authState.User.FindFirstValue(ClaimTypes.NameIdentifier), out benutzerId))
        {
            isLoading = false;
            return;
        }

        if (!aktiverSalon.IstGeladen)
        {
            await aktiverSalon.InitialisiereAsync(benutzerId);
        }

        if (aktiverSalon.Aktiv is not { } aktive)
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
        if (aktiverSalon.Aktiv is not { } aktive || aktive.SalonId == salonId)
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
        ladeFehler = null;
        try
        {
            formeln = await aboService.GetFormelPreiseAsync(salonId);
            vertrag = await vertragsService.GetLadenVertragAsync(benutzerId, salonId);
        }
        catch (InvalidOperationException ex)
        {
            // z. B. kein Tarif konfiguriert: Seite trotzdem anzeigen.
            ladeFehler = ex.Message;
        }
        catch (UnauthorizedAccessException)
        {
            ladeFehler = "Seul le propriétaire de l'établissement peut gérer l'abonnement.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Abonnement für Laden {SalonId} konnte nicht geladen werden.", salonId);
            ladeFehler = "L'abonnement ne peut pas être chargé pour le moment.";
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task OeffneWechselAsync(AboIntervall ziel)
    {
        wechselZiel = ziel;
        wechselVorschau = null;
        try
        {
            wechselVorschau = await vertragsService.GetWechselVorschauAsync(benutzerId, salonId, ziel);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            wechselZiel = null;
            toast.ZeigeFehler(ex.Message);
        }
    }

    private async Task WechsleFormelAsync()
    {
        if (wechselZiel is not AboIntervall ziel)
        {
            return;
        }

        try
        {
            var art = await vertragsService.WechsleAsync(benutzerId, salonId, ziel);
            wechselZiel = null;
            var name = VertragsRegeln.Bezeichnung(ziel).ToLowerInvariant();
            toast.ZeigeErfolg(art switch
            {
                VertragsService.WechselArt.SofortUmgewandelt => $"Facture en cours convertie en formule {name}.",
                VertragsService.WechselArt.Unveraendert => $"Vous gardez la formule {name}.",
                _ => $"Formule {name} enregistrée pour la prochaine période."
            });
            await LadeAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            wechselZiel = null;
            toast.ZeigeFehler(ex.Message);
        }
    }

    private void OeffneMeldung(VertragsService.ZahlungEintrag periode)
    {
        meldungFuer = periode;
        zahlungsNotiz = null;
        zahlungFehler = null;
    }

    private async Task MeldeZahlungAsync()
    {
        if (meldungFuer is null)
        {
            return;
        }

        try
        {
            await vertragsService.MeldeAsync(benutzerId, meldungFuer.Id, zahlungsNotiz);
            meldungFuer = null;
            toast.ZeigeErfolg("Paiement signalé. L'administrateur confirmera la réception.");
            await LadeAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            zahlungFehler = ex.Message;
        }
    }

    private AboService.FormelPreis? Formel(AboIntervall intervall) => formeln.FirstOrDefault(f => f.Intervall == intervall);

    private static string StatusText(AboZahlungStatus status) => status switch
    {
        AboZahlungStatus.Offen => "À payer",
        AboZahlungStatus.Gemeldet => "Signalé · en vérification",
        AboZahlungStatus.Bestaetigt => "Payé",
        AboZahlungStatus.Abgelehnt => "Refusé · à payer",
        _ => status.ToString()
    };

    private static string StatusKlasse(AboZahlungStatus status) => status switch
    {
        AboZahlungStatus.Bestaetigt => "enote-tag-ok",
        AboZahlungStatus.Gemeldet => "",
        _ => "enote-tag-warn"
    };
}
