using ENote.Core.DataModels.Katalog;
using ENote.WebApp.Components.Shared;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Leistungen(
    KatalogService katalogService,
    AktiverSalonState aktiverSalon,
    ToastService toast,
    AuthenticationStateProvider authStateProvider,
    NavigationManager navigationManager) : IDisposable
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
    private string? bildVorschau;
    private string? bildFehler;
    private Guid aktuellerBenutzerId;

    // Sammel-Artikel
    private bool istSammelArtikel;
    private string basisLeistungIdText = string.Empty;
    private decimal? basisAbzugMenge;
    private IReadOnlyList<EnoteSelectOption<string>> basisOptionen = [];
    private IReadOnlyList<EnoteSelectOption<string>> kategorieOptionen = [];
    private string kategorieIdText = string.Empty;

    // Suche/Filter
    private string leistungsSuche = string.Empty;
    private string kategorieFilter = string.Empty;
    private IReadOnlyList<EnoteSelectOption<string>> kategorieFilterOptionen = [];

    private bool kategorienOffen;
    private string neueKategorieName = string.Empty;
    private List<LeistungKategorie> kategorien = [];

    private bool kategorieUmbenennenOffen;
    private bool kategorieLoeschenOffen;
    private string? kategorieFehler;
    private LeistungKategorie? aktiveKategorie;
    private LeistungKategorie? zuLoeschendeKategorie;
    private string kategorieNeuerName = string.Empty;

    private bool etikettDialogOffen;
    private Leistung? etikettLeistung;
    private string EtikettDialogTitel => $"Étiquettes – {etikettLeistung?.Name}";
    private int etikettAnzahl = 24;
    private string etikettFormat = "a4";
    private readonly IReadOnlyList<EnoteRadioOption<string>> etikettFormatOptionen =
    [
        new() { Value = "a4", Label = "Feuille A4 (plusieurs)" },
        new() { Value = "einzel", Label = "Étiquette 62×29 mm" }
    ];

    protected override async Task OnInitializedAsync()
    {
        aktiverSalon.OnChange += HandleSalonWechsel;

        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            isLoading = false;
            return;
        }

        aktuellerBenutzerId = userId;
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
        darfVerwalten = await katalogService.DarfLeistungenVerwaltenAsync(userId);
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
        leistungen = await katalogService.GetLeistungenAsync(salonId);
        basisOptionen = leistungen
            .Where(l => l.HatStueckzahl && l.BasisLeistungId is null)
            .Select(l => new EnoteSelectOption<string> { Value = l.Id.ToString(), Label = $"{l.Name} ({l.Stueckzahl} en stock)" })
            .ToList();
        var kategorienListe = await katalogService.GetKategorienAsync(salonId);
        kategorien = kategorienListe;
        kategorieOptionen = kategorienListe
            .Select(k => new EnoteSelectOption<string> { Value = k.Id.ToString(), Label = k.Name })
            .ToList();
        kategorieFilterOptionen = new[] { new EnoteSelectOption<string> { Value = "", Label = "Toutes les catégories" } }
            .Concat(kategorieOptionen)
            .ToList();
        isLoading = false;
    }

    private IEnumerable<Leistung> GefilterteLeistungen()
    {
        IEnumerable<Leistung> q = leistungen;

        if (!string.IsNullOrWhiteSpace(leistungsSuche))
        {
            var s = leistungsSuche.Trim();
            q = q.Where(l => l.Name.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(kategorieFilter))
        {
            q = q.Where(l => l.KategorieId?.ToString() == kategorieFilter);
        }

        return q;
    }

    private void NeuAnlegen()
    {
        bearbeitungsLeistung = new Leistung { SalonId = salonId, IstAktiv = true };
        istSammelArtikel = false;
        basisLeistungIdText = string.Empty;
        basisAbzugMenge = null;
        kategorieIdText = string.Empty;
        editorFehler = null;
        bildFehler = null;
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
            Kaufpreis = leistung.Kaufpreis,
            KategorieId = leistung.KategorieId,
            HatStueckzahl = leistung.HatStueckzahl,
            Stueckzahl = leistung.Stueckzahl,
            MindestbestandWarnung = leistung.MindestbestandWarnung,
            IstAktiv = leistung.IstAktiv,
            BildAssetId = leistung.BildAssetId,
            BasisLeistungId = leistung.BasisLeistungId,
            BasisAbzugMenge = leistung.BasisAbzugMenge
        };
        istSammelArtikel = leistung.BasisLeistungId is not null;
        basisLeistungIdText = leistung.BasisLeistungId?.ToString() ?? string.Empty;
        basisAbzugMenge = leistung.BasisAbzugMenge;
        kategorieIdText = leistung.KategorieId?.ToString() ?? string.Empty;
        editorFehler = null;
        bildFehler = null;
        bildVorschau = await katalogService.GetLeistungsBildUrlAsync(leistung.BildAssetId);
        editorOffen = true;
        StateHasChanged();
    }

    private byte[]? ausgewaehltesBildBytes;
    private string ausgewaehltesBildContentType = "image/jpeg";
    private string ausgewaehltesBildName = "image.jpg";

    private void HandleBildBereit((byte[] Bytes, string ContentType, string DateiName) bild)
    {
        bildFehler = null;
        ausgewaehltesBildBytes = bild.Bytes;
        ausgewaehltesBildContentType = bild.ContentType;
        ausgewaehltesBildName = bild.DateiName; // nicht mehr als IBrowserFile nötig
    }

    private void HandleBildEntfernt()
    {
        bildVorschau = null;
        ausgewaehltesBildBytes = null;
    }

    private void OeffneKategorieUmbenennen(LeistungKategorie kategorie)
    {
        aktiveKategorie = kategorie;
        kategorieNeuerName = kategorie.Name;
        kategorieFehler = null;
        kategorieUmbenennenOffen = true;
    }

    private async Task KategorieUmbenennenAsync()
    {
        if (aktiveKategorie is null)
        {
            return;
        }

        var (erfolg, fehler) = await katalogService.BenenneKategorieUmAsync(aktiveKategorie.Id, salonId, kategorieNeuerName);
        if (!erfolg)
        {
            kategorieFehler = fehler;
            return;
        }

        kategorieUmbenennenOffen = false;
        toast.ZeigeErfolg("Catégorie renommée.");
        await LadeAsync();
    }

    private void LoescheKategorieFragen(LeistungKategorie kategorie)
    {
        zuLoeschendeKategorie = kategorie;
        kategorieLoeschenOffen = true;
    }

    private async Task KategorieLoeschenAsync()
    {
        if (zuLoeschendeKategorie is null)
        {
            return;
        }

        await katalogService.LoescheKategorieAsync(zuLoeschendeKategorie.Id, salonId);
        kategorieLoeschenOffen = false;
        toast.ZeigeInfo("Catégorie supprimée.");
        await LadeAsync();
    }

    private async Task FuegeKategorieHinzuAsync()
    {
        if (string.IsNullOrWhiteSpace(neueKategorieName))
        {
            return;
        }

        await katalogService.SpeichereKategorieAsync(salonId, neueKategorieName);
        neueKategorieName = string.Empty;
        toast.ZeigeErfolg("Catégorie ajoutée.");
        await LadeAsync();
    }

    private void OeffneEtikettDialog(Leistung leistung)
    {
        etikettLeistung = leistung;
        etikettAnzahl = 24;
        etikettFormat = "a4";
        etikettDialogOffen = true;
    }

    private void DruckeEtiketten()
    {
        if (etikettLeistung is null)
        {
            return;
        }

        var url = $"/etiketten?salonId={salonId}&leistungId={etikettLeistung.Id}&anzahl={etikettAnzahl}&format={etikettFormat}";
        navigationManager.NavigateTo(url, forceLoad: true);
        etikettDialogOffen = false;
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

        // Sammel-Artikel auflösen/validieren.
        if (istSammelArtikel)
        {
            if (!Guid.TryParse(basisLeistungIdText, out var basisId))
            {
                editorFehler = "Veuillez choisir un stock de base.";
                return;
            }

            if (basisAbzugMenge is null or <= 0)
            {
                editorFehler = "Veuillez saisir la quantité retirée par vente.";
                return;
            }

            bearbeitungsLeistung.BasisLeistungId = basisId;
            bearbeitungsLeistung.BasisAbzugMenge = basisAbzugMenge;
        }
        else
        {
            bearbeitungsLeistung.BasisLeistungId = null;
            bearbeitungsLeistung.BasisAbzugMenge = null;
        }

        bearbeitungsLeistung.KategorieId = Guid.TryParse(kategorieIdText, out var katId) ? katId : null;

        bearbeitungsLeistung.SalonId = salonId;
        var gespeichert = await katalogService.SpeichereLeistungAsync(bearbeitungsLeistung);

        if (ausgewaehltesBildBytes is not null)
        {
            using var ms = new MemoryStream(ausgewaehltesBildBytes);
            var (bildErfolg, bildFehlerText, _) = await katalogService.SpeichereLeistungsBildAsync(
                gespeichert.Id, ausgewaehltesBildName, ausgewaehltesBildContentType, ms, aktuellerBenutzerId);
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
        => l.HatStueckzahl && l.MindestbestandWarnung is decimal min && l.Stueckzahl <= min;

    private string MargeText()
    {
        if (bearbeitungsLeistung.Kaufpreis is not decimal kauf || bearbeitungsLeistung.Preis <= 0)
        {
            return string.Empty;
        }

        var marge = bearbeitungsLeistung.Preis - kauf;
        var prozent = Math.Round(marge / bearbeitungsLeistung.Preis * 100m, 0);
        return $"{marge:N0} F ({prozent:N0} %)";
    }

    private static string BestandKlasse(Leistung l)
        => BestandWarnung(l) ? "enote-bestand enote-bestand-warn" : "enote-bestand";
}
