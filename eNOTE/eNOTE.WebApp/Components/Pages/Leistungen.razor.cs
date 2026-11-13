using ENote.Core.DataModels.Katalog;
using ENote.Core.Services.Katalog;
using ENote.WebApp.Components.Shared;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Leistungen(
    KatalogService katalogService,
    BestandsService bestandsService,
    AktiverSalonState aktiverSalon,
    ToastService toast,
    AuthenticationStateProvider authStateProvider,
    NavigationManager navigationManager,
    LadeZustand ladeZustand) : IDisposable
{
    private List<Leistung> leistungen = [];
    private bool isLoading = true;
    private bool darfVerwalten;
    private Guid salonId;

    private bool editorOffen;
    private bool loeschDialogOffen;
    private string? editorFehler;
    private Core.Services.Katalog.BarcodeRegeln.Pruefung? barcodePruefung;

    /// <summary>Live-Hinweis am Barcode-Feld (Format, Prüfziffer, Dublette) nach Eingabe/Scan.</summary>
    private async Task PruefeBarcodeAsync()
        => barcodePruefung = await katalogService.PruefeBarcodeAsync(salonId, bearbeitungsLeistung.StrichCode, bearbeitungsLeistung.Id);

    private async Task GeneriereBarcodeAsync()
    {
        bearbeitungsLeistung.StrichCode = await katalogService.GeneriereNeuenBarcodeAsync(salonId);
        await PruefeBarcodeAsync();
    }
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
    private IReadOnlyList<EnoteSelectOption<string>> kategorieZuordnungOptionen = [];
    private string kategorieIdText = string.Empty;

    // Suche/Filter
    private string leistungsSuche = string.Empty;
    private string kategorieFilter = string.Empty;
    private IReadOnlyList<EnoteSelectOption<string>> kategorieFilterOptionen = [];

    private bool kategorienOffen;
    private string neueKategorieName = string.Empty;
    private List<LeistungKategorie> kategorien = [];

    // Kategorie-Baum (Haupt + Unter) und Vorschau
    private List<KatalogService.KategorieBaum> kategorieBaum = [];
    private HashSet<Guid> favoritenIds = [];
    private Dictionary<Guid, string> bildUrls = [];
    private bool inventurOffen;

    // Verlust / Abschreibung
    private Leistung? verlustArtikel;
    private decimal verlustMenge = 1;
    private Core.DataModels.Katalog.AbschreibungsGrund verlustGrund = Core.DataModels.Katalog.AbschreibungsGrund.Casse;
    private string? verlustNotiz;
    private string? verlustFehler;

    private void OeffneVerlust(Leistung leistung)
    {
        verlustArtikel = leistung;
        verlustMenge = 1;
        verlustGrund = Core.DataModels.Katalog.AbschreibungsGrund.Casse;
        verlustNotiz = null;
        verlustFehler = null;
    }

    private async Task SchreibeAbAsync()
    {
        if (verlustArtikel is null) return;
        var ergebnis = await bestandsService.SchreibeAbAsync(aktuellerBenutzerId, verlustArtikel.Id, verlustMenge, verlustGrund, verlustNotiz);
        if (!ergebnis.Erfolg)
        {
            verlustFehler = ergebnis.Fehler;
            return;
        }

        toast.ZeigeInfo($"{verlustMenge:0.##} × « {verlustArtikel.Name} » retiré(s) du stock.");
        verlustArtikel = null;
        await LadeAsync();
    }

    /// <summary>Lagerwert (Einkauf/Verkauf) und möglicher Gewinn aller Stückartikel.</summary>
    private Core.Services.Katalog.LagerRechner.Bewertung Lager => Core.Services.Katalog.LagerRechner.Bewerte(leistungen);

    private Guid? aufgeklappteKategorieId;
    private Guid? unterkategorieParentId; // für das Anlegen einer Unterkategorie
    private string neueUnterkategorieName = string.Empty;
    private string? unterkategorieFehler;

    // Kategorie-Zuordnungs-Modal (schöner als Dropdown auf der Karte)
    private bool zuordnungModalOffen;
    private Leistung? zuordnungLeistung;
    private string zuordnungEntwurf = string.Empty; // Entwurfs-Auswahl (wird erst mit Bestätigen übernommen)

    private bool kategorieUmbenennenOffen;
    private bool kategorieLoeschenOffen;
    private string? kategorieFehler;
    private LeistungKategorie? aktiveKategorie;
    private LeistungKategorie? zuLoeschendeKategorie;
    private string kategorieNeuerName = string.Empty;
    private string? kategorieNeueFarbe; // null = automatisch / wie Hauptkategorie

    /// <summary>Farbe, die die bearbeitete Kategorie ohne eigene Wahl hätte.</summary>
    private string AutoFarbeAktiveKategorie => aktiveKategorie switch
    {
        null => KategorieFarben.Neutral,
        { ParentId: Guid parentId } => KategorieFarben.Ermittle(kategorien, parentId),
        var k => KategorieFarben.Automatisch(kategorien, k.Id),
    };

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
        using var _ = ladeZustand.Beginnen();
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
        kategorieBaum = await katalogService.GetKategorieBaumAsync(salonId);
        favoritenIds = await katalogService.GetFavoritenIdsAsync(salonId);
        bildUrls = await katalogService.GetLeistungsBildUrlsAsync(salonId);
        kategorieOptionen = kategorienListe
            .Select(k => new EnoteSelectOption<string>
            {
                Value = k.Id.ToString(),
                Label = k.ParentId is null ? k.Name : $"↳ {k.Name}"
            })
            .ToList();
        kategorieZuordnungOptionen = new[] { new EnoteSelectOption<string> { Value = "", Label = "Sans catégorie" } }
            .Concat(kategorienListe.Select(k => new EnoteSelectOption<string>
            {
                Value = k.Id.ToString(),
                Label = k.ParentId is null ? k.Name : $"   ↳ {k.Name}"
            }))
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
            q = q.Where(l => l.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
                || Core.Services.Katalog.BarcodeRegeln.Gleich(l.StrichCode, s));
        }
        else if (Guid.TryParse(kategorieFilter, out var filterId))
        {
            // Hauptkategorie zeigt auch die Artikel ihrer Unterkategorien.
            var ids = kategorien.Where(k => k.Id == filterId || k.ParentId == filterId).Select(k => k.Id).ToHashSet();
            q = q.Where(l => l.KategorieId is Guid k && ids.Contains(k));
        }

        return q;
    }

    private async void NeuAnlegen()
    {
        bearbeitungsLeistung = new Leistung { Id = Guid.Empty, SalonId = salonId, IstAktiv = true, Typ = LeistungsTyp.Prestation };
        istSammelArtikel = false;
        basisLeistungIdText = string.Empty;
        basisAbzugMenge = null;
        kategorieIdText = string.Empty;
        editorFehler = null;
        bildFehler = null;
        bildVorschau = null;
        editorOffen = true;

        // Leer lassen: Hersteller-Code kann gescannt werden; sonst wird beim Speichern automatisch ein Code erzeugt.
        barcodePruefung = Core.Services.Katalog.BarcodeRegeln.Pruefe(null);
        StateHasChanged();
    }

    private async void Bearbeiten(Leistung leistung)
    {
        bearbeitungsLeistung = new Leistung
        {
            Id = leistung.Id,
            SalonId = leistung.SalonId,
            Name = leistung.Name,
            Beschreibung = leistung.Beschreibung,
            Typ = leistung.Typ,
            Preis = leistung.Preis,
            Kaufpreis = leistung.Kaufpreis,
            KategorieId = leistung.KategorieId,
            HatStueckzahl = leistung.HatStueckzahl,
            Stueckzahl = leistung.Stueckzahl,
            MindestbestandWarnung = leistung.MindestbestandWarnung,
            IstAktiv = leistung.IstAktiv,
            BildAssetId = leistung.BildAssetId,
            BasisLeistungId = leistung.BasisLeistungId,
            BasisAbzugMenge = leistung.BasisAbzugMenge,
            // Vorhandenen Code übernehmen – sonst würde jedes Speichern ihn ersetzen (gedruckte Etiketten ungültig).
            StrichCode = leistung.StrichCode
        };
        barcodePruefung = Core.Services.Katalog.BarcodeRegeln.Pruefe(leistung.StrichCode);
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

    // Typ-Wechsel: Prestation <-> Produit. Setzt ungültige Felder zurück.
    private void SetzeTyp(LeistungsTyp typ)
    {
        if (bearbeitungsLeistung.Typ == typ) return;
        bearbeitungsLeistung.Typ = typ;

        if (typ == LeistungsTyp.Prestation)
        {
            // Prestation: kein Kaufpreis, kein Stock, kein Sammel-Artikel.
            bearbeitungsLeistung.Kaufpreis = null;
            bearbeitungsLeistung.HatStueckzahl = false;
            istSammelArtikel = false;
            basisLeistungIdText = string.Empty;
            basisAbzugMenge = null;
        }
    }

    // Eigener Stock: schließt Sammel-Artikel aus (gegenseitiger Ausschluss).
    private async Task SetzeHatStueckzahl(bool wert)
    {
        bearbeitungsLeistung.HatStueckzahl = wert;
        if (wert)
        {
            // Eigener Stock: kein Sammel-Artikel.
            istSammelArtikel = false;
            basisLeistungIdText = string.Empty;
            basisAbzugMenge = null;
        }
        await Task.CompletedTask;
    }

    // Sammel-Artikel: schließt eigenen Stock aus (gegenseitiger Ausschluss).
    private async Task SetzeSammelArtikel(bool wert)
    {
        istSammelArtikel = wert;
        if (wert)
        {
            // Sammel-Artikel: kein eigener Stock.
            bearbeitungsLeistung.HatStueckzahl = false;
        }
        await Task.CompletedTask;
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
        kategorieNeueFarbe = KategorieFarben.Normalisiere(kategorie.Farbe);
        kategorieFehler = null;
        kategorieUmbenennenOffen = true;
    }

    private async Task KategorieUmbenennenAsync()
    {
        if (aktiveKategorie is null)
        {
            return;
        }

        var (erfolg, fehler) = await katalogService.AktualisiereKategorieAsync(aktiveKategorie.Id, salonId, kategorieNeuerName, kategorieNeueFarbe);
        if (!erfolg)
        {
            kategorieFehler = fehler;
            return;
        }

        kategorieUmbenennenOffen = false;
        toast.ZeigeErfolg("Catégorie enregistrée.");
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

        var (erfolg, fehler) = await katalogService.LoescheKategorieAsync(zuLoeschendeKategorie.Id, salonId);
        if (!erfolg)
        {
            kategorieLoeschenOffen = false;
            toast.ZeigeFehler(fehler ?? "La suppression a échoué.");
            return;
        }
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
        Leistung gespeichert;
        try
        {
            gespeichert = await katalogService.SpeichereLeistungAsync(bearbeitungsLeistung);
        }
        catch (InvalidOperationException ex)
        {
            editorFehler = ex.Message;
            await PruefeBarcodeAsync();
            return;
        }

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
            try
            {
                await katalogService.LoescheLeistungAsync(zuLoeschendeLeistung.Id);
                toast.ZeigeErfolg("Prestation supprimée.");
            }
            catch (ArgumentException ex)
            {
                toast.ZeigeFehler(ex.Message);
            }
        }

        loeschDialogOffen = false;
        await LadeAsync();
    }

    private void SchliesseEditor() => editorOffen = false;
    private void SchliesseLoeschen() => loeschDialogOffen = false;

    // --- Kategorie-Baum: Auf-/Zuklappen, Unterkategorie anlegen ---
    private void ToggleAufklappen(Guid kategorieId)
        => aufgeklappteKategorieId = aufgeklappteKategorieId == kategorieId ? null : kategorieId;

    private void OeffneUnterkategorieDialog(Guid parentId)
    {
        unterkategorieParentId = parentId;
        neueUnterkategorieName = string.Empty;
        unterkategorieFehler = null;
        // Kategorie automatisch aufklappen, damit das Eingabefeld sichtbar wird.
        aufgeklappteKategorieId = parentId;
    }

    private void SchliesseUnterkategorieDialog() => unterkategorieParentId = null;

    private async Task FuegeUnterkategorieHinzuAsync()
    {
        if (unterkategorieParentId is not Guid parentId || string.IsNullOrWhiteSpace(neueUnterkategorieName))
        {
            return;
        }

        var (erfolg, fehler, _) = await katalogService.SpeichereUnterkategorieAsync(salonId, parentId, neueUnterkategorieName);
        if (!erfolg)
        {
            unterkategorieFehler = fehler;
            return;
        }

        unterkategorieParentId = null;
        neueUnterkategorieName = string.Empty;
        toast.ZeigeErfolg("Sous-catégorie ajoutée.");
        await LadeAsync();
    }

    // Anzahl der Leistungen in einer Kategorie (für die Baum-Anzeige).
    /// <summary>Anzahl Artikel einer Kategorie inklusive ihrer Unterkategorien.</summary>
    private int AnzahlInKategorie(Guid kategorieId)
    {
        var ids = kategorien.Where(k => k.Id == kategorieId || k.ParentId == kategorieId).Select(k => k.Id).ToHashSet();
        return leistungen.Count(l => !l.IstStandardArtikel && l.KategorieId is Guid k && ids.Contains(k));
    }

    private void FilterKategorie(Guid id)
    {
        kategorieFilter = id.ToString();
        leistungsSuche = string.Empty;
    }

    /// <summary>Gleiche Kategoriefarben wie in der Kasse (Wiedererkennung).</summary>
    private string KategorieFarbe(Guid? kategorieId) => KategorieFarben.Ermittle(kategorien, kategorieId);

    // Name der Kategorie einer Leistung (für den Zuordnungs-Button auf der Karte).
    private string KategorieNameVon(Leistung leistung)
        => leistung.KategorieId is Guid id
            ? kategorien.FirstOrDefault(k => k.Id == id)?.Name ?? "Sans catégorie"
            : "Sans catégorie";

    // --- Zuordnung Leistung → Kategorie (nur Besitzer, hier zentral, über schönes Modal) ---
    private void OeffneZuordnungModal(Leistung leistung)
    {
        zuordnungLeistung = leistung;
        zuordnungEntwurf = leistung.KategorieId?.ToString() ?? string.Empty; // aktuelle Auswahl als Entwurf
        zuordnungModalOffen = true;
    }

    private void SchliesseZuordnungModal() => zuordnungModalOffen = false;

    private void WaehleZuordnungEntwurf(string kategorieIdText) => zuordnungEntwurf = kategorieIdText;

    private async Task BestaetigeZuordnungAsync()
    {
        if (zuordnungLeistung is null) return;
        Guid? katId = Guid.TryParse(zuordnungEntwurf, out var g) && g != Guid.Empty ? g : null;
        var erfolg = await katalogService.SetzeLeistungKategorieAsync(zuordnungLeistung.Id, salonId, katId);
        if (erfolg)
        {
            zuordnungLeistung.KategorieId = katId;
            toast.ZeigeErfolg("Catégorie mise à jour.");
        }
        zuordnungModalOffen = false;
    }

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
