using ENote.Core.DataModels.Katalog;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Leistungen(KatalogService katalogService, ToastService toast, AuthenticationStateProvider authStateProvider)
{
    private List<Leistung> leistungen = [];
    private bool isLoading = true;
    private bool darfVerwalten;
    private Guid salonId;

    private bool editorOffen;
    private bool loeschDialogOffen;
    private string? editorFehler;
    private Leistung bearbeitungsLeistung = new();
    private Leistung? zuLoeschendeLeistung;
    private IBrowserFile? ausgewaehltesBild;
    private string? bildVorschau;
    private string? bildFehler;
    private Guid aktuellerBenutzerId;

    protected override async Task OnInitializedAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            isLoading = false;
            return;
        }

        aktuellerBenutzerId = userId;

        var zuordnung = await katalogService.GetUserSalonAsync(userId);
        if (zuordnung is null)
        {
            isLoading = false;
            return;
        }

        salonId = zuordnung.SalonId;
        darfVerwalten = await katalogService.DarfLeistungenVerwaltenAsync(userId);
        await LadeAsync();
    }

    private async Task LadeAsync()
    {
        isLoading = true;
        leistungen = await katalogService.GetLeistungenAsync(salonId);
        isLoading = false;
    }

    private void NeuAnlegen()
    {
        bearbeitungsLeistung = new Leistung { SalonId = salonId, IstAktiv = true };
        editorFehler = null;
        bildFehler = null;
        ausgewaehltesBild = null;
        bildVorschau = null;
        editorOffen = true;
    }

    private async void Bearbeiten(Leistung leistung)
    {
        bearbeitungsLeistung = new Leistung
        {
            Id = leistung.Id,
            SalonId = leistung.SalonId,
            Name = leistung.Name,
            Beschreibung = leistung.Beschreibung,
            Preis = leistung.Preis,
            HatStueckzahl = leistung.HatStueckzahl,
            Stueckzahl = leistung.Stueckzahl,
            MindestbestandWarnung = leistung.MindestbestandWarnung,
            IstAktiv = leistung.IstAktiv,
            BildAssetId = leistung.BildAssetId
        };
        editorFehler = null;
        bildFehler = null;
        ausgewaehltesBild = null;
        bildVorschau = await katalogService.GetLeistungsBildUrlAsync(leistung.BildAssetId);
        editorOffen = true;
        StateHasChanged();
    }

    private async Task HandleBildAuswahl(InputFileChangeEventArgs e)
    {
        bildFehler = null;
        ausgewaehltesBild = null;
        var datei = e.File;

        if (!datei.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            bildFehler = "Veuillez choisir une image.";
            return;
        }

        if (datei.Size > 8 * 1024 * 1024)
        {
            bildFehler = "L'image dépasse la limite de 8 Mo.";
            return;
        }

        ausgewaehltesBild = datei;

        // Vorschau als Data-URL (nur für die Anzeige im Dialog, nicht zum Speichern).
        await using var stream = datei.OpenReadStream(8 * 1024 * 1024);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        bildVorschau = $"data:{datei.ContentType};base64,{Convert.ToBase64String(ms.ToArray())}";
    }

    private async Task SpeichernAsync()
    {
        if (string.IsNullOrWhiteSpace(bearbeitungsLeistung.Name))
        {
            editorFehler = "Veuillez saisir un nom.";
            return;
        }

        if (bearbeitungsLeistung.Preis < 0)
        {
            editorFehler = "Le prix ne peut pas être négatif.";
            return;
        }

        bearbeitungsLeistung.SalonId = salonId;
        var gespeichert = await katalogService.SpeichereLeistungAsync(bearbeitungsLeistung);

        if (ausgewaehltesBild is not null)
        {
            await using var stream = ausgewaehltesBild.OpenReadStream(8 * 1024 * 1024);
            var (bildErfolg, bildFehlerText, _) = await katalogService.SpeichereLeistungsBildAsync(
                gespeichert.Id, ausgewaehltesBild.Name, ausgewaehltesBild.ContentType, stream, aktuellerBenutzerId);
            if (!bildErfolg)
            {
                toast.ZeigeFehler(bildFehlerText ?? "L'enregistrement de l'image a échoué.");
            }
        }

        editorOffen = false;
        toast.ZeigeErfolg("Prestation enregistrée.");
        await LadeAsync();
    }

    private void LoeschenFragen(Leistung leistung)
    {
        zuLoeschendeLeistung = leistung;
        loeschDialogOffen = true;
    }

    private async Task LoeschenAsync()
    {
        if (zuLoeschendeLeistung is not null)
        {
            await katalogService.LoescheLeistungAsync(zuLoeschendeLeistung.Id);
            toast.ZeigeErfolg("Prestation supprimée.");
        }

        loeschDialogOffen = false;
        await LadeAsync();
    }

    private void SchliesseEditor() => editorOffen = false;
    private void SchliesseLoeschen() => loeschDialogOffen = false;

    private static bool BestandWarnung(Leistung l)
        => l.HatStueckzahl && l.MindestbestandWarnung is int min && l.Stueckzahl <= min;

    private static string BestandKlasse(Leistung l)
        => BestandWarnung(l) ? "enote-bestand enote-bestand-warn" : "enote-bestand";
}
