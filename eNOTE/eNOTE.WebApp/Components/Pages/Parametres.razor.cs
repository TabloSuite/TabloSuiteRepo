using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Parametres(
    SalonService salonService,
    KatalogService katalogService,
    TeamService teamService,
    ToastService toast,
    AktiverSalonState aktiverSalon,
    AuthenticationStateProvider authStateProvider) : IDisposable
{
    private bool isLoading = true;
    private Guid salonId;
    private Guid benutzerId;
    private string salonName = string.Empty;
    private string? slogan;
    private string? telefon;
    private string? adresse;
    private string? bonQrLink;
    private string? logoVorschau;
    private string? logoFehler;

    private string neueBesitzerPin = string.Empty;
    private string? pinFehler;
    private bool pinGespeichert;
    private bool hatBesitzerPin;
    private bool pinSichtbar;

    // Weiteren Laden hinzufügen
    private bool darfWeiterenLaden;
    private bool neuerLadenDialogOffen;
    private string neuerLadenName = string.Empty;
    private string? neuerLadenStandort;
    private string? neuerLadenFehler;

    protected override async Task OnInitializedAsync()
    {
        aktiverSalon.OnChange += HandleSalonWechsel;

        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out benutzerId))
        {
            isLoading = false;
            return;
        }

        if (!aktiverSalon.IstGeladen)
        {
            await aktiverSalon.InitialisiereAsync(benutzerId);
        }

        var aktive = aktiverSalon.Aktiv;
        if (aktive is null)
        {
            isLoading = false;
            return;
        }

        salonId = aktive.SalonId;
        await LadeSalonAsync();
        isLoading = false;
    }

    private void HandleSalonWechsel()
    {
        var aktive = aktiverSalon.Aktiv;
        if (aktive is null || aktive.SalonId == salonId)
        {
            return;
        }

        salonId = aktive.SalonId;
        _ = InvokeAsync(async () => { await LadeSalonAsync(); StateHasChanged(); });
    }

    public void Dispose() => aktiverSalon.OnChange -= HandleSalonWechsel;

    private async Task LadeSalonAsync()
    {
        var salon = await salonService.GetSalonAsync(salonId);
        if (salon is not null)
        {
            salonName = salon.Name;
            slogan = salon.Slogan;
            telefon = salon.TelefonNummer;
            adresse = salon.Adresse;
            bonQrLink = salon.BonQrLink;
            logoVorschau = salonService.GetLogoUrl(salon);
        }

        hatBesitzerPin = await salonService.HatBesitzerPinAsync(salonId, benutzerId);
        darfWeiterenLaden = await salonService.DarfWeiterenLadenAnlegenAsync(benutzerId);
    }

    private void OeffneNeuerLadenDialog()
    {
        neuerLadenName = string.Empty;
        neuerLadenStandort = null;
        neuerLadenFehler = null;
        neuerLadenDialogOffen = true;
    }

    private async Task FuegeNeuenLadenHinzuAsync()
    {
        var (erfolg, fehler, salon) = await salonService.FuegeSalonHinzuAsync(benutzerId, neuerLadenName, neuerLadenStandort, null);
        if (!erfolg)
        {
            neuerLadenFehler = fehler;
            return;
        }

        neuerLadenDialogOffen = false;
        await aktiverSalon.AktualisiereLaedenAsync(benutzerId);
        toast.ZeigeErfolg($"Établissement « {salon!.Name} » créé. Il sera activé après validation par l'administrateur.");
    }

    private byte[]? ausgewaehltesLogoBytes;
    private string ausgewaehltesLogoContentType = "image/jpeg";
    private string ausgewaehltesLogoName = "logo.jpg";

    private void HandleLogoBereit((byte[] Bytes, string ContentType, string DateiName) bild)
    {
        logoFehler = null;
        ausgewaehltesLogoBytes = bild.Bytes;
        ausgewaehltesLogoContentType = bild.ContentType;
        ausgewaehltesLogoName = bild.DateiName;
        logoVorschau = $"data:{bild.ContentType};base64,{Convert.ToBase64String(bild.Bytes)}";
    }

    private void HandleLogoEntfernt()
    {
        logoVorschau = null;
        ausgewaehltesLogoBytes = null;
    }

    private async Task SpeichernAsync()
    {
        await salonService.SetzeKontaktdatenAsync(salonId, slogan, telefon, adresse, bonQrLink);

        if (ausgewaehltesLogoBytes is not null)
        {
            using var ms = new MemoryStream(ausgewaehltesLogoBytes);
            var (erfolg, fehler, url) = await salonService.SetzeLogoAsync(
                salonId, ausgewaehltesLogoName, ausgewaehltesLogoContentType, ms, benutzerId);
            if (!erfolg)
            {
                toast.ZeigeFehler(fehler ?? "L'enregistrement du logo a échoué.");
                return;
            }

            logoVorschau = url;
        }

        toast.ZeigeErfolg("Paramètres enregistrés.");
    }

    private async Task SpeichereBesitzerPinAsync()
    {
        pinFehler = null;
        pinGespeichert = false;

        if (string.IsNullOrWhiteSpace(neueBesitzerPin))
        {
            pinFehler = "Veuillez saisir un code PIN.";
            return;
        }

        var zuordnung = await katalogService.GetUserSalonAsync(benutzerId);
        if (zuordnung is null)
        {
            pinFehler = "Membre introuvable.";
            return;
        }

        var (erfolg, fehler) = await teamService.SetzeBesitzerPinAsync(zuordnung.Id, neueBesitzerPin);
        if (!erfolg)
        {
            pinFehler = fehler;
            return;
        }

        neueBesitzerPin = string.Empty;
        hatBesitzerPin = true;
        pinGespeichert = true;
        toast.ZeigeErfolg("Code PIN propriétaire enregistré.");
    }
}
