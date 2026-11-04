using ENote.Core.DataModels.Salons;
using ENote.WebApp.Components.Shared;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Equipe(
    TeamService teamService,
    KatalogService katalogService,
    GeraetFavoritService geraetFavoritService,
    ToastService toast,
    AuthenticationStateProvider authStateProvider)
{
    private List<TeamService.TeamMitglied> team = [];
    private List<UserSalon> salons = [];
    private IReadOnlyList<EnoteSelectOption<string>> salonOptionen = [];
    private bool isLoading = true;
    private Guid salonId;
    private string salonName = string.Empty;
    private string favoritSalonId = string.Empty;

    private bool anlegedialogOffen;
    private bool pinDialogOffen;
    private bool rechteDialogOffen;
    private bool entfernenDialogOffen;
    private string? dialogFehler;
    private TeamService.TeamMitglied? aktivesMitglied;

    private string neuVorname = string.Empty;
    private string neuNachname = string.Empty;
    private string neuEmail = string.Empty;
    private string neuPasswort = string.Empty;
    private string neuPin = string.Empty;

    private bool rechteFinanzen;
    private bool rechteLeistungen;
    private bool rechteRabatte;

    protected override async Task OnInitializedAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            isLoading = false;
            return;
        }

        salons = await katalogService.GetUserSalonsAsync(userId);
        salonOptionen = salons.Select(s => new EnoteSelectOption<string> { Value = s.SalonId.ToString(), Label = s.Salon.Name }).ToList();
        var aktive = salons.FirstOrDefault(s => s.Salon.IstFreigegeben) ?? salons.FirstOrDefault();
        if (aktive is null)
        {
            isLoading = false;
            return;
        }

        salonId = aktive.SalonId;
        salonName = aktive.Salon.Name;
        favoritSalonId = salonId.ToString();

        try
        {
            var favorit = await geraetFavoritService.GetFavoritSalonIdAsync();
            if (favorit is Guid f)
            {
                favoritSalonId = f.ToString();
            }
        }
        catch (InvalidOperationException)
        {
            // JS noch nicht verfügbar (Prerendering).
        }

        await LadeAsync();
    }

    private async Task LadeAsync()
    {
        isLoading = true;
        team = await teamService.GetTeamAsync(salonId);
        isLoading = false;
    }

    private void NeuAnlegen()
    {
        neuVorname = neuNachname = neuEmail = neuPin = string.Empty;
        neuPasswort = ErzeugeInitialPasswort();
        dialogFehler = null;
        anlegedialogOffen = true;
    }

    private async Task LegeKassiererAnAsync()
    {
        if (string.IsNullOrWhiteSpace(neuVorname) || string.IsNullOrWhiteSpace(neuNachname)
            || string.IsNullOrWhiteSpace(neuEmail) || string.IsNullOrWhiteSpace(neuPasswort))
        {
            dialogFehler = "Veuillez remplir tous les champs.";
            return;
        }

        var (erfolg, fehler, _) = await teamService.LegeKassiererAnAsync(
            salonId, neuVorname, neuNachname, neuEmail, neuPasswort, neuPin);

        if (!erfolg)
        {
            dialogFehler = fehler;
            return;
        }

        anlegedialogOffen = false;
        toast.ZeigeErfolg("Caissier créé. Communiquez-lui son mot de passe initial et son code PIN.");
        await LadeAsync();
    }

    private void OeffnePinDialog(TeamService.TeamMitglied mitglied)
    {
        aktivesMitglied = mitglied;
        neuPin = string.Empty;
        dialogFehler = null;
        pinDialogOffen = true;
    }

    private async Task SetzePinAsync()
    {
        if (aktivesMitglied is null)
        {
            return;
        }

        var (erfolg, fehler) = await teamService.SetzeKassenPinAsync(aktivesMitglied.UserSalonId, neuPin);
        if (!erfolg)
        {
            dialogFehler = fehler;
            return;
        }

        pinDialogOffen = false;
        toast.ZeigeErfolg("Code PIN enregistré.");
        await LadeAsync();
    }

    private void OeffneRechteDialog(TeamService.TeamMitglied mitglied)
    {
        aktivesMitglied = mitglied;
        rechteFinanzen = mitglied.KannFinanzenSehen;
        rechteLeistungen = mitglied.KannLeistungenVerwalten;
        rechteRabatte = mitglied.KannRabatteVerwalten;
        rechteDialogOffen = true;
    }

    private async Task SetzeRechteAsync()
    {
        if (aktivesMitglied is null)
        {
            return;
        }

        await teamService.SetzeRechteAsync(
            aktivesMitglied.UserSalonId, rechteFinanzen, rechteLeistungen, rechteRabatte);
        rechteDialogOffen = false;
        toast.ZeigeErfolg("Droits enregistrés.");
        await LadeAsync();
    }

    private void OeffneEntfernenDialog(TeamService.TeamMitglied mitglied)
    {
        aktivesMitglied = mitglied;
        entfernenDialogOffen = true;
    }

    private async Task EntferneMitgliedAsync()
    {
        if (aktivesMitglied is null)
        {
            return;
        }

        await teamService.EntferneMitgliedAsync(aktivesMitglied.UserSalonId);
        entfernenDialogOffen = false;
        toast.ZeigeErfolg("Membre retiré du salon.");
        await LadeAsync();
    }

    private async Task SetzeFavoritAsync()
    {
        if (Guid.TryParse(favoritSalonId, out var id))
        {
            await geraetFavoritService.SetzeFavoritAsync(id, null);
            toast.ZeigeErfolg("Salon favori défini sur cet appareil.");
        }
    }

    private async Task EntferneFavoritAsync()
    {
        await geraetFavoritService.EntferneFavoritAsync();
        toast.ZeigeInfo("Salon favori retiré de cet appareil.");
    }

    private void SchliesseDialoge()
    {
        anlegedialogOffen = pinDialogOffen = rechteDialogOffen = entfernenDialogOffen = false;
        dialogFehler = null;
    }

    private static string ErzeugeInitialPasswort()
    {
        const string zeichen = "abcdefghjkmnpqrstuvwxyz23456789";
        var zufall = new Random();
        return new string(Enumerable.Range(0, 10).Select(_ => zeichen[zufall.Next(zeichen.Length)]).ToArray());
    }
}
