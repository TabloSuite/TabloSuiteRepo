using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Parametres(
    SalonService salonService,
    KatalogService katalogService,
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
    private IBrowserFile? ausgewaehltesLogo;

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

        isLoading = false;
    }

    private async Task HandleLogoAuswahl(InputFileChangeEventArgs e)
    {
        logoFehler = null;
        ausgewaehltesLogo = null;
        var datei = e.File;

        if (!datei.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            logoFehler = "Veuillez choisir une image.";
            return;
        }

        if (datei.Size > 8 * 1024 * 1024)
        {
            logoFehler = "L'image dépasse la limite de 8 Mo.";
            return;
        }

        ausgewaehltesLogo = datei;
        await using var stream = datei.OpenReadStream(8 * 1024 * 1024);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        logoVorschau = $"data:{datei.ContentType};base64,{Convert.ToBase64String(ms.ToArray())}";
    }

    private async Task SpeichernAsync()
    {
        await salonService.SetzeKontaktdatenAsync(salonId, slogan, telefon, adresse, bonQrLink);

        if (ausgewaehltesLogo is not null)
        {
            await using var stream = ausgewaehltesLogo.OpenReadStream(8 * 1024 * 1024);
            var (erfolg, fehler, url) = await salonService.SetzeLogoAsync(
                salonId, ausgewaehltesLogo.Name, ausgewaehltesLogo.ContentType, stream, benutzerId);
            if (!erfolg)
            {
                toast.ZeigeFehler(fehler ?? "L'enregistrement du logo a échoué.");
                return;
            }

            logoVorschau = url;
            ausgewaehltesLogo = null;
        }

        toast.ZeigeErfolg("Paramètres enregistrés.");
    }
}
