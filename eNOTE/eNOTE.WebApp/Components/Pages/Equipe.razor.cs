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
    AktiverSalonState aktiverSalon,
    ToastService toast,
    AuthenticationStateProvider authStateProvider,
    LadeZustand ladeZustand) : IDisposable
{
    private List<TeamService.TeamMitglied> team = [];
    private List<UserSalon> salons = [];
    private bool isLoading = true;
    private Guid salonId;
    private Guid benutzerId;
    private string salonName = string.Empty;

    // Suche
    private string teamSuche = string.Empty;

    private IEnumerable<TeamService.TeamMitglied> GefiltertesTeam()
    {
        if (string.IsNullOrWhiteSpace(teamSuche))
        {
            return team;
        }

        var s = teamSuche.Trim();
        return team.Where(m =>
            m.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
            || m.Email.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    private bool anlegedialogOffen;
    private bool pinDialogOffen;
    private bool rechteDialogOffen;
    private bool entfernenDialogOffen;
    private string? dialogFehler;
    private TeamService.TeamMitglied? aktivesMitglied;

    private string neuVorname = string.Empty;
    private string neuNachname = string.Empty;
    private string neuPseudonym = string.Empty;
    private string neuEmail = string.Empty;
    private string neuPin = string.Empty;

    private bool rechteFinanzen;
    private bool rechteLeistungen;
    private bool rechteRabatte;
    private bool rechteErstatten;
    // « Connexion personnelle » (17.11.2026): eigenes Konto nur mit diesem Recht; Passwort erst beim Einschalten.
    private bool rechteAnmeldung;
    private string anmeldungPasswort = string.Empty;
    private bool AnmeldungPasswortNoetig => rechteAnmeldung && aktivesMitglied is { } m && (!m.KannSichAnmelden || !m.HatPasswort);

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

        salons = aktiverSalon.Laeden;
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
        team = await teamService.GetTeamAsync(salonId);
        isLoading = false;
    }

    private void NeuAnlegen()
    {
        neuVorname = neuNachname = neuPseudonym = neuEmail = neuPin = string.Empty;
        dialogFehler = null;
        anlegedialogOffen = true;
    }

    private async Task LegeKassiererAnAsync()
    {
        if (string.IsNullOrWhiteSpace(neuVorname) || string.IsNullOrWhiteSpace(neuNachname)
            || string.IsNullOrWhiteSpace(neuPseudonym) || string.IsNullOrWhiteSpace(neuEmail))
        {
            dialogFehler = "Veuillez remplir tous les champs.";
            return;
        }

        var (erfolg, fehler, _) = await teamService.LegeKassiererAnAsync(
            salonId, neuVorname, neuNachname, neuPseudonym, neuEmail, null, neuPin);

        if (!erfolg)
        {
            dialogFehler = fehler;
            return;
        }

        anlegedialogOffen = false;
        toast.ZeigeErfolg("Employé créé. Communiquez-lui son code PIN : il encaisse sur l'appareil de l'établissement.");
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
        rechteErstatten = mitglied.KannErstatten;
        rechteAnmeldung = mitglied.KannSichAnmelden;
        anmeldungPasswort = string.Empty;
        dialogFehler = null;
        rechteDialogOffen = true;
    }

    private async Task SetzeRechteAsync()
    {
        if (aktivesMitglied is null)
        {
            return;
        }

        if (AnmeldungPasswortNoetig && string.IsNullOrWhiteSpace(anmeldungPasswort))
        {
            dialogFehler = "Choisissez un mot de passe pour la connexion personnelle.";
            return;
        }

        await teamService.SetzeRechteAsync(
            aktivesMitglied.UserSalonId, rechteFinanzen, rechteLeistungen, rechteRabatte, rechteErstatten);
        if (rechteAnmeldung != aktivesMitglied.KannSichAnmelden || !string.IsNullOrWhiteSpace(anmeldungPasswort))
        {
            var (ok, fehler) = await teamService.SetzeAnmeldungAsync(aktivesMitglied.UserSalonId, rechteAnmeldung,
                rechteAnmeldung ? anmeldungPasswort : null);
            if (!ok)
            {
                dialogFehler = fehler;
                await LadeAsync();
                return;
            }
        }
        rechteDialogOffen = false;
        toast.ZeigeErfolg("Droits enregistrés.");
        await LadeAsync();
    }

    private static string PseudonymText(TeamService.TeamMitglied m)
        => m.Pseudonym is { Length: > 0 } p ? "@" + p : "pseudonyme";

    private void AnmeldungUmschalten(bool wert)
    {
        rechteAnmeldung = wert;
        dialogFehler = null;
        anmeldungPasswort = AnmeldungPasswortNoetig ? ErzeugeInitialPasswort() : string.Empty;
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

    private void SchliesseDialoge()
    {
        anlegedialogOffen = pinDialogOffen = rechteDialogOffen = entfernenDialogOffen = false;
        dialogFehler = null;
    }

    /// <summary>
    /// Initialpasswort: 10 gut lesbare Zeichen (ohne i/l/o/0/1), kryptografisch zufällig und
    /// garantiert mit Ziffer und Kleinbuchstabe (Passwortregel von Identity).
    /// </summary>
    private static string ErzeugeInitialPasswort()
    {
        const string buchstaben = "abcdefghjkmnpqrstuvwxyz";
        const string ziffern = "23456789";
        const string alle = buchstaben + ziffern;
        var zeichen = new char[10];
        for (var i = 0; i < zeichen.Length; i++)
            zeichen[i] = alle[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alle.Length)];
        zeichen[System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 5)] = buchstaben[System.Security.Cryptography.RandomNumberGenerator.GetInt32(buchstaben.Length)];
        zeichen[System.Security.Cryptography.RandomNumberGenerator.GetInt32(5, 10)] = ziffern[System.Security.Cryptography.RandomNumberGenerator.GetInt32(ziffern.Length)];
        return new string(zeichen);
    }

    private static readonly string[] AvatarPalette = ["#2547b8", "#0f8f86", "#c2410c", "#7c3aed", "#be185d", "#0369a1", "#4d7c0f", "#b45309"];

    private static string AvatarFarbe(Guid id) => AvatarPalette[(int)((uint)id.GetHashCode() % AvatarPalette.Length)];

    private static string Initialen(string name)
    {
        var teile = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return teile.Length switch
        {
            0 => "?",
            1 => teile[0][..Math.Min(2, teile[0].Length)].ToUpperInvariant(),
            _ => string.Concat(teile[0][0], teile[^1][0]).ToUpperInvariant()
        };
    }
}
