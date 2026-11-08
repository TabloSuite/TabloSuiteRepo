using ENote.Core.DataModels.Katalog;
using ENote.WebApp.Components.Shared;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Rabatte(KatalogService katalogService, AktiverSalonState aktiverSalon, ToastService toast, AuthenticationStateProvider authStateProvider, LadeZustand ladeZustand) : IDisposable
{
    private List<Rabatt> rabatte = [];
    private List<Leistung> leistungen = [];
    private bool isLoading = true;
    private bool darfVerwalten;
    private Guid salonId;
    private Guid benutzerId;

    // Suche
    private string rabattSuche = string.Empty;

    private IEnumerable<Rabatt> GefilterteRabatte()
    {
        if (string.IsNullOrWhiteSpace(rabattSuche))
        {
            return rabatte;
        }

        var s = rabattSuche.Trim();
        return rabatte.Where(r =>
            r.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
            || (r.Bedingung?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
            || (r.Leistung?.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private bool editorOffen;
    private bool loeschDialogOffen;
    private string? editorFehler;
    private Rabatt bearbeitungsRabatt = new();
    private Rabatt? zuLoeschenderRabatt;
    private string ausgewaehlteLeistungId = string.Empty;

    private bool hatGueltigkeit;
    private DateTime? gueltigVon;
    private DateTime? gueltigBis;

    private readonly IReadOnlyList<EnoteRadioOption<RabattArt>> rabattArtOptionen =
    [
        new() { Value = RabattArt.Prozent, Label = "Pourcentage" },
        new() { Value = RabattArt.Festbetrag, Label = "Montant fixe" }
    ];
    private IReadOnlyList<EnoteSelectOption<string>> leistungsOptionen = [];

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

        var aktive = aktiverSalon.Aktiv;
        if (aktive is null)
        {
            isLoading = false;
            return;
        }

        salonId = aktive.SalonId;
        darfVerwalten = await katalogService.DarfRabatteVerwaltenAsync(userId);
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
        _ = InvokeAsync(async () => { await LadeAsync(); StateHasChanged(); });
    }

    public void Dispose() => aktiverSalon.OnChange -= HandleSalonWechsel;

    private async Task LadeAsync()
    {
        isLoading = true;
        rabatte = await katalogService.GetRabatteAsync(salonId);
        leistungen = await katalogService.GetLeistungenAsync(salonId);
        leistungsOptionen = leistungen
            .Select(l => new EnoteSelectOption<string> { Value = l.Id.ToString(), Label = l.Name })
            .ToList();
        isLoading = false;
    }

    private void NeuAnlegen()
    {
        bearbeitungsRabatt = new Rabatt { SalonId = salonId, IstAktiv = false, Art = RabattArt.Prozent };
        ausgewaehlteLeistungId = string.Empty;
        hatGueltigkeit = false;
        gueltigVon = null;
        gueltigBis = null;
        editorFehler = null;
        editorOffen = true;
    }

    private void Bearbeiten(Rabatt rabatt)
    {
        bearbeitungsRabatt = new Rabatt
        {
            Id = rabatt.Id,
            SalonId = rabatt.SalonId,
            Name = rabatt.Name,
            Bedingung = rabatt.Bedingung,
            Art = rabatt.Art,
            Wert = rabatt.Wert,
            LeistungId = rabatt.LeistungId,
            IstAktiv = rabatt.IstAktiv,
            GueltigVonUtc = rabatt.GueltigVonUtc,
            GueltigBisUtc = rabatt.GueltigBisUtc
        };
        ausgewaehlteLeistungId = rabatt.LeistungId?.ToString() ?? string.Empty;
        hatGueltigkeit = rabatt.GueltigVonUtc is not null || rabatt.GueltigBisUtc is not null;
        gueltigVon = rabatt.GueltigVonUtc;
        gueltigBis = rabatt.GueltigBisUtc;
        editorFehler = null;
        editorOffen = true;
    }

    private async Task SpeichernAsync()
    {
        if (string.IsNullOrWhiteSpace(bearbeitungsRabatt.Name))
        {
            editorFehler = "Veuillez saisir un nom.";
            return;
        }

        if (bearbeitungsRabatt.Wert < 0)
        {
            editorFehler = "La valeur ne peut pas être négative.";
            return;
        }

        if (bearbeitungsRabatt.Art == RabattArt.Prozent && bearbeitungsRabatt.Wert > 100)
        {
            editorFehler = "Le pourcentage ne peut pas dépasser 100.";
            return;
        }

        bearbeitungsRabatt.SalonId = salonId;
        bearbeitungsRabatt.LeistungId = Guid.TryParse(ausgewaehlteLeistungId, out var leistungId) ? leistungId : null;
        bearbeitungsRabatt.GueltigVonUtc = hatGueltigkeit ? gueltigVon : null;
        bearbeitungsRabatt.GueltigBisUtc = hatGueltigkeit ? gueltigBis : null;

        await katalogService.SpeichereRabattAsync(bearbeitungsRabatt);
        editorOffen = false;
        toast.ZeigeErfolg("Remise enregistrée.");
        await LadeAsync();
    }

    private void LoeschenFragen(Rabatt rabatt)
    {
        zuLoeschenderRabatt = rabatt;
        loeschDialogOffen = true;
    }

    private async Task LoeschenAsync()
    {
        if (zuLoeschenderRabatt is not null)
        {
            await katalogService.LoescheRabattAsync(zuLoeschenderRabatt.Id);
            toast.ZeigeErfolg("Remise supprimée.");
        }

        loeschDialogOffen = false;
        await LadeAsync();
    }

    private void SchliesseEditor() => editorOffen = false;
    private void SchliesseLoeschen() => loeschDialogOffen = false;

    private static string RabattText(Rabatt r)
        => r.Art == RabattArt.Prozent ? $"-{r.Wert:N0} %" : $"-{r.Wert:N0} F";

    private static bool IstGueltig(Rabatt r)
    {
        var jetzt = DateTime.UtcNow;
        if (r.GueltigVonUtc is not null && jetzt < r.GueltigVonUtc) return false;
        if (r.GueltigBisUtc is not null && jetzt > r.GueltigBisUtc) return false;
        return true;
    }
}
