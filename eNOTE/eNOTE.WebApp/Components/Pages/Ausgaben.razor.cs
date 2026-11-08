using ENote.Core.DataModels.Finanzen;
using ENote.WebApp.Components.Shared;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Ausgaben(
    AusgabenService ausgabenService,
    KatalogService katalogService,
    AktiverSalonState aktiverSalon,
    ToastService toast,
    AuthenticationStateProvider authStateProvider,
    LadeZustand ladeZustand) : IDisposable
{
    private bool isLoading = true;
    private Guid salonId;
    private Guid benutzerId;
    private string salonName = string.Empty;

    private List<AusgabenKategorie> kategorien = [];
    private IReadOnlyList<EnoteSelectOption<string>> kategorienOptionen = [];
    private List<Ausgabe> ausgaben = [];
    private Dictionary<Guid, string> belegUrls = [];

    private DateTime? filterVon = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? filterBis = DateTime.Today;

    // Suche/Filter
    private string ausgabenSuche = string.Empty;
    private string kategorieFilter = string.Empty;
    private IReadOnlyList<EnoteSelectOption<string>> kategorieFilterOptionen = [];

    private IEnumerable<Ausgabe> GefilterteAusgaben()
    {
        IEnumerable<Ausgabe> q = ausgaben;

        if (!string.IsNullOrWhiteSpace(ausgabenSuche))
        {
            var s = ausgabenSuche.Trim();
            q = q.Where(a =>
                (a.Notiz?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                || a.Kategorie.Name.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(kategorieFilter))
        {
            q = q.Where(a => a.Kategorie.Id.ToString() == kategorieFilter);
        }

        return q;
    }

    private string neueKategorieName = string.Empty;

    private bool ausgabeDialogOffen;
    private string? ausgabeFehler;
    private decimal neuBetrag;
    private DateTime? neuDatum = DateTime.Today;
    private string neuKategorieId = string.Empty;
    private string? neuNotiz;
    private IBrowserFile? belegDatei;
    private string? belegName;

    protected override async Task OnInitializedAsync()
    {
        using var _ = ladeZustand.Beginnen();
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
        kategorien = await ausgabenService.GetKategorienAsync(salonId);
        kategorienOptionen = kategorien.Where(k => k.IstAktiv)
            .Select(k => new EnoteSelectOption<string> { Value = k.Id.ToString(), Label = k.Name })
            .ToList();
        kategorieFilterOptionen = new[] { new EnoteSelectOption<string> { Value = "", Label = "Toutes les catégories" } }
            .Concat(kategorienOptionen)
            .ToList();
        var bisEnde = (filterBis ?? DateTime.Today).Date.AddDays(1).AddTicks(-1);
        ausgaben = await ausgabenService.GetAusgabenAsync(salonId, (filterVon ?? DateTime.Today).Date, bisEnde);

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
            salonId, kategorieId, neuBetrag, (neuDatum ?? DateTime.Today).Date, neuNotiz, benutzerId, beleg);

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
