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
    AuthenticationStateProvider authStateProvider)
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

    protected override async Task OnInitializedAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out benutzerId))
        {
            isLoading = false;
            return;
        }

        var zuordnung = await katalogService.GetUserSalonAsync(benutzerId);
        if (zuordnung is null)
        {
            isLoading = false;
            return;
        }

        salonId = zuordnung.SalonId;
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
        isLoading = false;
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
