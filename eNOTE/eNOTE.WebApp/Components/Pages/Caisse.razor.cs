using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.WebApp.Components.Shared;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Caisse(
    KassenService kassenService,
    KatalogService katalogService,
    GeraetFavoritService geraetFavoritService,
    KassenAnzeigeState anzeigeState,
    IDisplayBenachrichtigungService displayService,
    ToastService toast,
    AuthenticationStateProvider authStateProvider,
    NavigationManager navigationManager,
    FinanzService finanzService,
    IJSRuntime js) : IDisposable
{
    // Start
    private List<(Guid UserId, string Name)> kassiererListe = [];
    private Guid ausgewaehlterKassiererId = Guid.Empty;
    private string pin = string.Empty;
    private decimal startbestand;
    private string? startFehler;

    // Aktive Kasse
    private KassenSitzung? aktiveSitzung;
    private string aktiverSalonName = string.Empty;
    private bool sitzungWiederhergestellt;
    private bool wiederhergestelltModalOffen;
    private string? wiederhergestelltKassierer;
    private decimal? wiederhergestelltSoll;
    private DateTime wiederhergestelltGestartet;
    private List<Leistung> leistungen = [];
    private List<Rabatt> rabatte = [];
    private IReadOnlyList<EnoteSelectOption<string>> rabattOptionen = [];
    private List<TicketPosition> ticket = [];
    private readonly Dictionary<Guid, int> tileMengen = [];

    private bool freierBetragOffen;
    private string? freierBetragFehler;
    private decimal freierBetrag;
    private int freierBetragMenge = 1;
    private Leistung? standardArtikel;

    private bool displayDialogOffen;
    private string? displayQrDataUrl;
    private string displayLink = string.Empty;

    private string? aktiveKategorie;
    private List<string> kategorien = [];
    private HashSet<Guid> favoritenIds = [];
    private Guid aktuellerBenutzerId;
    private bool istBesitzerOderAdmin;
    private IEnumerable<Leistung> GefilterteLeistungen =>
        (aktiveKategorie is null ? leistungen : leistungen.Where(l => l.Kategorie?.Name == aktiveKategorie))
        .OrderByDescending(l => favoritenIds.Contains(l.Id))
        .ThenBy(l => l.Name);

    // Zahlung
    private ZahlungsArt zahlungsArt = ZahlungsArt.Bar;
    private readonly IReadOnlyList<EnoteRadioOption<ZahlungsArt>> zahlungsArtOptionen =
    [
        new() { Value = ZahlungsArt.Bar, Label = "Espèces" },
        new() { Value = ZahlungsArt.Karte, Label = "Carte" }
    ];
    private decimal gegeben;
    private bool karteBestaetigt;
    private string ausgewaehlteRabattId = string.Empty;
    private bool rabattAktiviert;
    private bool bedingungBestaetigt;
    private string? verkaufFehler;

    // Zahlungs-Modale (platzsparend statt Inline-Zahlungsblock)
    private bool barModalOffen;
    private bool karteModalOffen;
    private bool abbrechenBestaetigungOffen;

    // Rückgeld im Bar-Modal live (unabhängig von zahlungsArt).
    private decimal rueckgeldModal => gegeben >= gesamtSumme ? Math.Round(gegeben - gesamtSumme, 2) : 0m;
    private bool barBetragZuWenig => gegeben < gesamtSumme;

    private void OeffneBarModal()
    {
        if (ticket.Count == 0)
        {
            return;
        }

        verkaufFehler = null;
        gegeben = gesamtSumme; // komfortabel: exakter Betrag vorbefüllt
        barModalOffen = true;
    }

    private void OeffneKarteModal()
    {
        if (ticket.Count == 0)
        {
            return;
        }

        verkaufFehler = null;
        karteBestaetigt = false;
        karteModalOffen = true;
    }

    private async Task BestaetigeBarAsync()
    {
        zahlungsArt = ZahlungsArt.Bar;
        await BucheVerkaufAsync();
        if (string.IsNullOrWhiteSpace(verkaufFehler))
        {
            barModalOffen = false;
        }
    }

    private async Task BestaetigeKarteAsync()
    {
        zahlungsArt = ZahlungsArt.Karte;
        karteBestaetigt = true;
        await BucheVerkaufAsync();
        if (string.IsNullOrWhiteSpace(verkaufFehler))
        {
            karteModalOffen = false;
        }
    }
    private Guid? letzterVerkaufId;
    private string scanCode = string.Empty;

    private string bedingungLabel =>
        ausgewaehlterRabatt?.Bedingung is string b && !string.IsNullOrWhiteSpace(b)
            ? "Condition remplie : " + b
            : "Condition remplie";

    // Abschluss
    private bool abschlussOffen;
    private decimal endbestandGezaehlt;
    private string? abschlussNotiz;
    private string? abschlussFehler;
    private (decimal Startbestand, decimal BarEinnahmen, decimal Soll)? barSollInfo;

    // Verkaufshistorie (Kassierer darf vorherige Verkäufe sehen + Bon nachdrucken)
    private bool historieOffen;
    private bool historieLaedt;
    private List<FinanzService.VerkaufEintrag> historieVerkaeufe = [];

    private async Task OeffneHistorieAsync()
    {
        historieOffen = true;
        if (aktiveSitzung is null)
        {
            return;
        }

        historieLaedt = true;
        historieVerkaeufe = await finanzService.GetLetzteVerkaeufeAsync(aktiveSitzung.SalonId, 50);
        historieLaedt = false;
    }

    private Rabatt? ausgewaehlterRabatt =>
        Guid.TryParse(ausgewaehlteRabattId, out var id) ? rabatte.FirstOrDefault(r => r.Id == id) : null;

    private decimal zwischensumme => ticket.Sum(p => p.EffektivPreis * p.Menge);
    private decimal rabattSumme => BerechneRabattSumme();
    private decimal gesamtSumme => Math.Max(zwischensumme - rabattSumme, 0m);

    protected override async Task OnInitializedAsync()
    {
        anzeigeState.OnChange += HandleAnzeigeStateChanged;

        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return;
        }

        aktuellerBenutzerId = userId;
        istBesitzerOderAdmin = authState.User.IsInRole(Core.DataModels.Identity.AppRollen.Besitzer)
            || authState.User.IsInRole(Core.DataModels.Identity.AppRollen.SuperAdmin)
            || authState.User.IsInRole(Core.DataModels.Identity.AppRollen.ProduktOwner);

        // Offene Sitzung eines der dem Nutzer zugeordneten Salons wiederherstellen.
        var zuordnungen = await katalogService.GetUserSalonsAsync(userId);
        foreach (var z in zuordnungen)
        {
            var offene = await kassenService.GetOffeneSitzungAsync(z.SalonId);
            if (offene is not null)
            {
                aktiveSitzung = offene;
                aktiverSalonName = z.Salon.Name;
                sitzungWiederhergestellt = true;
                var aufstellung = await kassenService.GetBarSollAufstellungAsync(offene.Id);
                anzeigeState.SitzungGestartet(offene.Id, offene.SalonId, offene.Kassierer.FullName, offene.Startbestand, aufstellung?.BarEinnahmen ?? 0m);
                await LadeKatalogAsync(offene.SalonId);
                wiederhergestelltKassierer = offene.Kassierer.FullName;
                wiederhergestelltSoll = aufstellung?.Soll;
                wiederhergestelltGestartet = offene.GestartetUtc;
                wiederhergestelltModalOffen = true;
                return;
            }
        }

        await LadeKassiererListeAsync(zuordnungen);
    }

    private async Task LadeKassiererListeAsync(List<Core.DataModels.Salons.UserSalon> zuordnungen)
    {
        // Kassierer aus allen dem Nutzer zugeordneten (freigegebenen) Salons sammeln.
        var distinct = new Dictionary<Guid, string>();
        foreach (var z in zuordnungen.Where(x => x.Salon.IstFreigegeben))
        {
            foreach (var k in await kassenService.GetKassiererAsync(z.SalonId))
            {
                distinct.TryAdd(k.UserId, k.Name);
            }
        }

        kassiererListe = distinct.Select(kv => (kv.Key, kv.Value)).OrderBy(x => x.Value).ToList();

        // Optionaler Geräte-Favorit: Salon vorbestimmen und dessen Kassierer zuerst zeigen.
        try
        {
            var favoritSalonId = await geraetFavoritService.GetFavoritSalonIdAsync();
            if (favoritSalonId is Guid salonId)
            {
                var favoritKassierer = await kassenService.GetKassiererAsync(salonId);
                var rest = kassiererListe.Where(k => favoritKassierer.All(f => f.UserId != k.UserId)).ToList();
                kassiererListe = favoritKassierer.Concat(rest).ToList();
            }
        }
        catch (InvalidOperationException)
        {
            // JS noch nicht verfügbar (Prerendering) – Favorit wird ignoriert.
        }
    }

    private async Task StarteSitzungAsync()
    {
        startFehler = null;

        if (ausgewaehlterKassiererId == Guid.Empty)
        {
            startFehler = "Veuillez choisir un caissier.";
            return;
        }

        if (string.IsNullOrWhiteSpace(pin))
        {
            startFehler = "Veuillez saisir le code PIN.";
            return;
        }

        // PIN löst die Zuordnung (und damit den richtigen Laden) auf.
        var (gefunden, findFehler, zuordnung, _) = await kassenService.FindeZuordnungPerPinAsync(ausgewaehlterKassiererId, pin);
        if (!gefunden || zuordnung is null)
        {
            startFehler = findFehler;
            return;
        }

        var (gestartet, startFehlerText, sitzung, _) = await kassenService.StarteSitzungAsync(
            zuordnung.SalonId, ausgewaehlterKassiererId, startbestand);

        if (!gestartet || sitzung is null)
        {
            startFehler = startFehlerText;
            return;
        }

        aktiveSitzung = sitzung;
        aktiverSalonName = zuordnung.Salon.Name;
        anzeigeState.SitzungGestartet(sitzung.Id, sitzung.SalonId, zuordnung.AppUser.FullName, sitzung.Startbestand);
        await LadeKatalogAsync(sitzung.SalonId);

        pin = string.Empty;
        toast.ZeigeErfolg($"Caisse ouverte – {zuordnung.Salon.Name}");
    }

    private async Task LadeKatalogAsync(Guid salonId)
    {
        leistungen = (await katalogService.GetLeistungenAsync(salonId)).Where(l => l.IstAktiv).ToList();
        rabatte = (await katalogService.GetRabatteAsync(salonId)).Where(r => r.IstAktiv).ToList();
        rabattOptionen = rabatte
            .Select(r => new EnoteSelectOption<string> { Value = r.Id.ToString(), Label = $"{r.Name} ({RabattText(r)})" })
            .ToList();
        kategorien = leistungen
            .Where(l => l.Kategorie is not null)
            .Select(l => l.Kategorie!.Name)
            .Distinct()
            .OrderBy(k => k)
            .ToList();
        favoritenIds = await katalogService.GetFavoritenIdsAsync(salonId);
    }

    private void AendereTileMenge(Guid leistungId, int delta)
    {
        var aktuell = tileMengen.GetValueOrDefault(leistungId, 1);
        var neu = Math.Clamp(aktuell + delta, 1, 99);
        tileMengen[leistungId] = neu;
    }

    private void FuegeHinzuMitMenge(Leistung leistung)
    {
        var menge = tileMengen.GetValueOrDefault(leistung.Id, 1);
        var vorhanden = ticket.FirstOrDefault(p => p.Leistung.Id == leistung.Id && p.Rabatt is null);
        if (vorhanden is not null)
        {
            vorhanden.Menge += menge;
        }
        else
        {
            ticket.Add(new TicketPosition { Leistung = leistung, Menge = menge });
        }

        tileMengen[leistung.Id] = 1; // zurücksetzen
        _ = SendeTicketAnDisplayAsync();
    }

    /// <summary>Standard-Artikel öffnet den Betrag-Dialog; normale Leistungen werden direkt hinzugefügt.</summary>
    private void WaehleLeistung(Leistung leistung)
    {
        if (leistung.IstStandardArtikel)
        {
            standardArtikel = leistung;
            freierBetrag = 0m;
            freierBetragMenge = tileMengen.GetValueOrDefault(leistung.Id, 1);
            freierBetragFehler = null;
            freierBetragOffen = true;
            return;
        }

        FuegeHinzuMitMenge(leistung);
    }

    private void FuegeStandardArtikelHinzu()
    {
        if (standardArtikel is null)
        {
            return;
        }

        if (freierBetrag <= 0)
        {
            freierBetragFehler = "Veuillez saisir un montant positif.";
            return;
        }

        ticket.Add(new TicketPosition { Leistung = standardArtikel, Menge = freierBetragMenge, FreierBetrag = freierBetrag });
        freierBetragOffen = false;
        _ = SendeTicketAnDisplayAsync();
    }

    private void FuegeHinzu(Leistung leistung)
    {
        var vorhanden = ticket.FirstOrDefault(p => p.Leistung.Id == leistung.Id && p.Rabatt is null);
        if (vorhanden is not null)
        {
            vorhanden.Menge++;
        }
        else
        {
            ticket.Add(new TicketPosition { Leistung = leistung, Menge = 1 });
        }
    }

    private void AendereMenge(TicketPosition pos, int delta)
    {
        pos.Menge += delta;
        if (pos.Menge <= 0)
        {
            ticket.Remove(pos);
        }

        _ = SendeTicketAnDisplayAsync();
    }

    private void EntfernePos(TicketPosition pos)
    {
        ticket.Remove(pos);
        _ = SendeTicketAnDisplayAsync();
    }

    /// <summary>Öffnet die Bestätigung zum Abbrechen des aktuellen Verkaufs.</summary>
    private void OeffneAbbrechenBestaetigung()
    {
        if (ticket.Count == 0)
        {
            return;
        }

        abbrechenBestaetigungOffen = true;
    }

    /// <summary>Bricht den aktuellen Verkauf ab: entfernt alle Positionen und setzt Rabatt/Eingaben zurück.</summary>
    private void BrecheTicketAb()
    {
        ticket.Clear();
        tileMengen.Clear();
        ausgewaehlteRabattId = string.Empty;
        rabattAktiviert = false;
        bedingungBestaetigt = false;
        gegeben = 0m;
        karteBestaetigt = false;
        freierBetragOffen = false;
        freierBetragFehler = null;
        abbrechenBestaetigungOffen = false;
        _ = SendeTicketAnDisplayAsync();
    }

    /// <summary>Sendet den aktuellen Ticket-Stand an das Kunden-Display (falls verbunden).</summary>
    private async Task SendeTicketAnDisplayAsync()
    {
        if (aktiveSitzung is null)
        {
            return;
        }

        var positionen = ticket.Select(p => new DisplayPosition(p.Leistung.Name, p.Menge, ZeilenSumme(p))).ToList();
        var ticketDto = new DisplayTicket(positionen, zwischensumme, rabattSumme, gesamtSumme, Abgeschlossen: false, Rueckgeld: null);
        try
        {
            await displayService.TicketAktualisiertAsync(aktiveSitzung.Id, ticketDto);
        }
        catch
        {
            // Display-Benachrichtigung darf die Kasse nie blockieren.
        }
    }

    /// <summary>
    /// Beendet den Kassen-Modus (nur für Besitzer/Admin sichtbar), ohne die
    /// Sitzung zu schließen. Der Kassierer kann später per PIN weitermachen.
    /// </summary>
    private async Task ToggleFavoritAsync(Guid leistungId)
    {
        if (aktiveSitzung is null || aktuellerBenutzerId == Guid.Empty)
        {
            return;
        }

        await katalogService.ToggleFavoritAsync(aktiveSitzung.SalonId, leistungId, aktuellerBenutzerId);
        favoritenIds = await katalogService.GetFavoritenIdsAsync(aktiveSitzung.SalonId);
    }

    private void HandleScanTaste(KeyboardEventArgs e)
    {
        if (e.Key != "Enter" || string.IsNullOrWhiteSpace(scanCode))
        {
            return;
        }

        var code = scanCode.Trim();
        var treffer = leistungen.FirstOrDefault(l =>
            string.Equals(l.StrichCode, code, StringComparison.OrdinalIgnoreCase));

        if (treffer is not null)
        {
            FuegeHinzu(treffer);
        }
        else
        {
            toast.ZeigeFehler($"Code-barres inconnu : {code}");
        }

        scanCode = string.Empty;
    }

    private decimal BerechneRabattSumme()
    {
        var rabatt = ausgewaehlterRabatt;
        if (rabatt is null || !rabattAktiviert)
        {
            return 0m;
        }

        // Leistungsbezogener Rabatt gilt nur auf Positionen dieser Leistung,
        // globaler Rabatt auf den ganzen Bon.
        var basis = rabatt.LeistungId is Guid leistungId
            ? ticket.Where(p => p.Leistung.Id == leistungId).Sum(p => p.EffektivPreis * p.Menge)
            : zwischensumme;

        var nachRabatt = Core.Services.Kasse.KassenRechner.WendeRabattAn(basis, rabatt.Art, rabatt.Wert);
        return Math.Round(basis - nachRabatt, 2);
    }

    private decimal ZeilenSumme(TicketPosition pos)
    {
        var rabatt = ausgewaehlterRabatt;
        if (rabatt is not null && (rabatt.LeistungId is null || rabatt.LeistungId == pos.Leistung.Id))
        {
            var betrag = pos.EffektivPreis * pos.Menge;
            // Bei globalem Rabatt verteilen wir nicht pro Zeile in der Anzeige.
            if (rabatt.LeistungId is not null)
            {
                return Core.Services.Kasse.KassenRechner.WendeRabattAn(betrag, rabatt.Art, rabatt.Wert);
            }
        }

        return pos.EffektivPreis * pos.Menge;
    }

    private async Task BucheVerkaufAsync()
    {
        verkaufFehler = null;

        if (aktiveSitzung is null)
        {
            verkaufFehler = "Aucune caisse ouverte.";
            return;
        }

        var rabatt = rabattAktiviert ? ausgewaehlterRabatt : null;
        if (rabatt is not null && !string.IsNullOrWhiteSpace(rabatt.Bedingung) && !bedingungBestaetigt)
        {
            verkaufFehler = "Veuillez confirmer la condition de la remise.";
            return;
        }

        var positionen = ticket.Select(p => new KassenService.VerkaufsPositionInput(
            p.Leistung.Id,
            p.Menge,
            rabatt is not null && (rabatt.LeistungId is null || rabatt.LeistungId == p.Leistung.Id) ? rabatt.Id : null,
            bedingungBestaetigt,
            p.FreierBetrag)).ToList();

        var (erfolg, fehler, verkauf, warnungen) = await kassenService.BucheVerkaufAsync(
            aktiveSitzung.Id, positionen, zahlungsArt,
            zahlungsArt == ZahlungsArt.Bar ? gegeben : null,
            karteBestaetigt);

        if (!erfolg || verkauf is null)
        {
            verkaufFehler = fehler;
            return;
        }

        anzeigeState.VerkaufGebucht(verkauf.ZahlungsArt == ZahlungsArt.Bar ? verkauf.GesamtSumme : 0m);
        letzterVerkaufId = verkauf.Id;
        foreach (var warnung in warnungen)
        {
            toast.ZeigeInfo(warnung);
        }

        toast.ZeigeErfolg(zahlungsArt == ZahlungsArt.Bar
            ? $"Encaissé. Monnaie à rendre : {verkauf.Rueckgeld:N0} F"
            : "Paiement par carte confirmé.");

        // Kunden-Display: abgeschlossenen Verkauf zeigen (Total + Rückgeld), dann zurücksetzen.
        if (aktiveSitzung is not null)
        {
            var abgeschlossenesTicket = new DisplayTicket(
                verkauf.Positionen.Select(p => new DisplayPosition(p.LeistungName, p.Menge, p.ZeilenSumme)).ToList(),
                verkauf.Zwischensumme,
                verkauf.RabattSumme,
                verkauf.GesamtSumme,
                Abgeschlossen: true,
                Rueckgeld: verkauf.Rueckgeld);
            _ = displayService.TicketAktualisiertAsync(aktiveSitzung.Id, abgeschlossenesTicket);
        }

        // Ticket zurücksetzen.
        ticket.Clear();
        gegeben = 0m;
        karteBestaetigt = false;
        ausgewaehlteRabattId = string.Empty;
        rabattAktiviert = false;
        bedingungBestaetigt = false;
        zahlungsArt = ZahlungsArt.Bar;

        await LadeKatalogAsync(aktiveSitzung.SalonId);
    }

    /// <summary>Öffnet den Kunden-Display-Dialog mit QR-Code und Link auf die laufende Sitzung.</summary>
    private void OeffneDisplayDialog()
    {
        if (aktiveSitzung is null)
        {
            return;
        }

        displayLink = navigationManager.ToAbsoluteUri($"/display/{aktiveSitzung.Id}").ToString();
        displayQrDataUrl = ErzeugeQrDataUrl(displayLink);
        displayDialogOffen = true;
    }

    /// <summary>Erzeugt einen QR-Code als PNG-Data-URL.</summary>
    private static string? ErzeugeQrDataUrl(string inhalt)
    {
        try
        {
            using var generator = new QRCoder.QRCodeGenerator();
            using var daten = generator.CreateQrCode(inhalt, QRCoder.QRCodeGenerator.ECCLevel.M);
            var png = new QRCoder.PngByteQRCode(daten);
            return "data:image/png;base64," + Convert.ToBase64String(png.GetGraphic(20));
        }
        catch
        {
            return null;
        }
    }

    private async Task OeffneAbschluss()
    {
        abschlussFehler = null;
        endbestandGezaehlt = 0m;
        abschlussNotiz = null;
        barSollInfo = aktiveSitzung is not null ? await kassenService.GetBarSollAufstellungAsync(aktiveSitzung.Id) : null;
        abschlussOffen = true;
    }

    private async Task SchliesseSitzungAsync()
    {
        if (aktiveSitzung is null)
        {
            return;
        }

        var (erfolg, fehler, sitzung) = await kassenService.SchliesseSitzungAsync(
            aktiveSitzung.Id, endbestandGezaehlt, abschlussNotiz);

        if (!erfolg || sitzung is null)
        {
            abschlussFehler = fehler;
            return;
        }

        abschlussOffen = false;
        var differenzText = sitzung.Differenz >= 0 ? $"+{sitzung.Differenz:N0}" : $"{sitzung.Differenz:N0}";
        toast.ZeigeErfolg($"Caisse clôturée. Attendu : {sitzung.BareinnahmenSoll:N0} F, compté : {sitzung.EndbestandGezaehlt:N0} F (écart {differenzText} F).");

        aktiveSitzung = null;
        anzeigeState.SitzungBeendet();

        // Zurück zum Start: Kassiererliste neu laden.
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdValue, out var userId))
        {
            var zuordnungen = await katalogService.GetUserSalonsAsync(userId);
            await LadeKassiererListeAsync(zuordnungen);
        }
    }

    /// <summary>Öffnet den Browserdruck; im eingerichteten Windows-Kioskprofil ohne Druckdialog.</summary>
    private async Task DruckeBonAsync(Guid verkaufId)
    {
        await js.InvokeVoidAsync("enoteOeffneTab", $"/bon/{verkaufId}/druck");
    }

    private void HandleAnzeigeStateChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => anzeigeState.OnChange -= HandleAnzeigeStateChanged;

    private sealed class TicketPosition
    {
        public Leistung Leistung { get; set; } = null!;
        public int Menge { get; set; }
        public Rabatt? Rabatt { get; set; }

        /// <summary>Freier Betrag beim Standard-Artikel „Divers“ (ersetzt den Preis).</summary>
        public decimal? FreierBetrag { get; set; }

        /// <summary>Effektiver Einzelpreis (FreierBetrag beim Standard-Artikel, sonst Leistung.Preis).</summary>
        public decimal EffektivPreis => FreierBetrag ?? Leistung.Preis;
    }

    private static string RabattText(Rabatt r)
        => r.Art == RabattArt.Prozent ? $"-{r.Wert:N0} %" : $"-{r.Wert:N0} F";
}
