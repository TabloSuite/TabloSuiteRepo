using ENote.Core.DataModels.Finanzen;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Ausgaben(
    AusgabenService ausgabenService,
    KatalogService katalogService,
    ToastService toast,
    AuthenticationStateProvider authStateProvider)
{
    private bool isLoading = true;
    private Guid salonId;
    private Guid benutzerId;
    private string salonName = string.Empty;

    private List<AusgabenKategorie> kategorien = [];
    private List<Ausgabe> ausgaben = [];
    private Dictionary<Guid, string> belegUrls = [];

    private DateTime filterVon = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime filterBis = DateTime.Today;

    private bool kategorienOffen;
    private string neueKategorieName = string.Empty;

    private bool ausgabeDialogOffen;
    private string? ausgabeFehler;
    private decimal neuBetrag;
    private DateTime neuDatum = DateTime.Today;
    private string neuKategorieId = string.Empty;
    private string? neuNotiz;
    private IBrowserFile? belegDatei;
    private string? belegName;

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
        salonName = zuordnung.Salon.Name;
        await LadeAsync();
    }

    private async Task LadeAsync()
    {
        isLoading = true;
        kategorien = await ausgabenService.GetKategorienAsync(salonId);
        var bisEnde = filterBis.Date.AddDays(1).AddTicks(-1);
        ausgaben = await ausgabenService.GetAusgabenAsync(salonId, filterVon.Date, bisEnde);

        belegUrls = new Dictionary<Guid, string>();
        foreach (var a in ausgaben.Where(x => x.BelegAssetId is not null))
        {
            var url = await ausgabenService.GetBelegUrlAsync(a.BelegAssetId);
            if (url is not null)
            {
                belegUrls[a.Id] = url;
            }
        }

        isLoading = false;
    }

    private async Task FuegeKategorieHinzuAsync()
    {
        if (string.IsNullOrWhiteSpace(neueKategorieName))
        {
            return;
        }

        await ausgabenService.SpeichereKategorieAsync(salonId, null, neueKategorieName, istAktiv: true);
        neueKategorieName = string.Empty;
        toast.ZeigeErfolg("Catégorie ajoutée.");
        await LadeAsync();
    }

    private void NeueAusgabe()
    {
        neuBetrag = 0m;
        neuDatum = DateTime.Today;
        neuKategorieId = string.Empty;
        neuNotiz = null;
        belegDatei = null;
        belegName = null;
        ausgabeFehler = null;
        ausgabeDialogOffen = true;
    }

    private void HandleBelegAuswahl(InputFileChangeEventArgs e)
    {
        if (e.File.Size > 8 * 1024 * 1024)
        {
            ausgabeFehler = "Le justificatif dépasse la limite de 8 Mo.";
            belegDatei = null;
            return;
        }

        belegDatei = e.File;
        belegName = e.File.Name;
        ausgabeFehler = null;
    }

    private async Task SpeichereAusgabeAsync()
    {
        if (neuBetrag <= 0)
        {
            ausgabeFehler = "Le montant doit être positif.";
            return;
        }

        if (!Guid.TryParse(neuKategorieId, out var kategorieId))
        {
            ausgabeFehler = "Veuillez choisir une catégorie.";
            return;
        }

        (string DateiName, string ContentType, Stream Inhalt)? beleg = null;
        if (belegDatei is not null)
        {
            beleg = (belegDatei.Name, belegDatei.ContentType, belegDatei.OpenReadStream(8 * 1024 * 1024));
        }

        var (erfolg, fehler, _) = await ausgabenService.ErfasseAusgabeAsync(
            salonId, kategorieId, neuBetrag, neuDatum.Date, neuNotiz, benutzerId, beleg);

        beleg?.Inhalt.Dispose();

        if (!erfolg)
        {
            ausgabeFehler = fehler;
            return;
        }

        ausgabeDialogOffen = false;
        toast.ZeigeErfolg("Dépense enregistrée.");
        await LadeAsync();
    }

    private async Task LoescheAusgabeAsync(Guid id)
    {
        await ausgabenService.LoescheAusgabeAsync(id, salonId);
        toast.ZeigeInfo("Dépense supprimée.");
        await LadeAsync();
    }
}
