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
    private string statusFilter = string.Empty;

    private IEnumerable<Rabatt> GefilterteRabatte()
    {
        IEnumerable<Rabatt> liste = statusFilter switch
        {
            "aktiv" => rabatte.Where(r => r.IstAktiv),
            "aus" => rabatte.Where(r => !r.IstAktiv),
            _ => rabatte
        };
        liste = liste.OrderByDescending(r => r.IstAktiv).ThenBy(r => r.Name);

        if (string.IsNullOrWhiteSpace(rabattSuche))
        {
            return liste;
        }

        var s = rabattSuche.Trim();
        return liste.Where(r =>
            r.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
            || (r.Bedingung?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
            || (r.Leistung?.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private bool editorOffen;
    private bool loeschDialogOffen;
    private string? editorFehler;
    private Rabatt bearbeitungsRabatt = new();
    /// <summary>Editor für eine neue Remise (die Id ist schon vorbelegt, daher eigenes Kennzeichen).</summary>
    private bool istNeueRemise;
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
        istNeueRemise = true;
        // Id leer = neu anlegen (der Dienst legt nur mit Guid.Empty an; das Modell vergibt sonst schon eine Id
        // und das Speichern liefe in den Aktualisierungszweig: « Rabatt nicht gefunden »)
        bearbeitungsRabatt = new Rabatt { Id = Guid.Empty, SalonId = salonId, IstAktiv = false, Art = RabattArt.Prozent };
        ausgewaehlteLeistungId = string.Empty;
        hatGueltigkeit = false;
        gueltigVon = null;
        gueltigBis = null;
        editorFehler = null;
        editorOffen = true;
    }

    private void Bearbeiten(Rabatt rabatt)
    {
        istNeueRemise = false;
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

    /// <summary>Schalter direkt auf der Karte: aktiviert/deaktiviert ohne Editor.</summary>
    private async Task SetzeAktivAsync(Rabatt rabatt, bool aktiv)
    {
        if (!darfVerwalten) return;
        var kopie = new Rabatt
        {
            Id = rabatt.Id, SalonId = rabatt.SalonId, Name = rabatt.Name, Bedingung = rabatt.Bedingung, Art = rabatt.Art,
            Wert = rabatt.Wert, LeistungId = rabatt.LeistungId, IstAktiv = aktiv,
            GueltigVonUtc = rabatt.GueltigVonUtc, GueltigBisUtc = rabatt.GueltigBisUtc
        };
        await katalogService.SpeichereRabattAsync(kopie);
        rabatt.IstAktiv = aktiv;
        toast.ZeigeInfo(aktiv ? $"« {rabatt.Name} » est active en caisse." : $"« {rabatt.Name} » est désactivée.");
    }

    /// <summary>Rechenbeispiel im Editor, damit der Besitzer die Wirkung sofort sieht.</summary>
    private string BeispielText()
    {
        var leistung = Guid.TryParse(ausgewaehlteLeistungId, out var id) ? leistungen.FirstOrDefault(l => l.Id == id) : null;
        var basis = leistung?.Preis is decimal p && p > 0 ? p : 10000m;
        var nach = Core.Services.Kasse.KassenRechner.WendeRabattAn(basis, bearbeitungsRabatt.Art, bearbeitungsRabatt.Wert);
        var wo = leistung is null ? "un ticket de" : $"« {leistung.Name} » à";
        return $"{wo} {basis:N0} F → {nach:N0} F (−{basis - nach:N0} F)";
    }

    private static string PeriodeText(Rabatt r)
    {
        var von = r.GueltigVonUtc?.ToString("dd.MM.yyyy");
        var bis = r.GueltigBisUtc?.ToString("dd.MM.yyyy");
        return (von, bis) switch
        {
            (not null, not null) => $"Du {von} au {bis}",
            (not null, null) => $"À partir du {von}",
            (null, not null) => $"Jusqu'au {bis}",
            _ => string.Empty
        };
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
            var geloescht = await katalogService.LoescheRabattAsync(zuLoeschenderRabatt.Id);
            if (geloescht) toast.ZeigeErfolg("Remise supprimée.");
            else toast.ZeigeInfo("Remise déjà utilisée sur des reçus : elle a été désactivée.");
        }

        loeschDialogOffen = false;
        await LadeAsync();
    }

    private void SchliesseEditor() => editorOffen = false;
    private void SchliesseLoeschen() => loeschDialogOffen = false;

    private static string RabattText(Rabatt r)
        => r.Art == RabattArt.Prozent ? $"−{r.Wert:0.##} %" : $"−{r.Wert:N0} F";

    private static bool IstGueltig(Rabatt r)
    {
        var jetzt = DateTime.UtcNow;
        if (r.GueltigVonUtc is not null && jetzt < r.GueltigVonUtc) return false;
        if (r.GueltigBisUtc is not null && jetzt > r.GueltigBisUtc) return false;
        return true;
    }
}
