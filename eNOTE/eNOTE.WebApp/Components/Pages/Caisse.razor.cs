using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Caisse(
    KassenService kassenService,
    KatalogService katalogService,
    GeraetFavoritService geraetFavoritService,
    KassenAnzeigeState anzeigeState,
    ToastService toast,
    AuthenticationStateProvider authStateProvider)
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
    private List<Leistung> leistungen = [];
    private List<Rabatt> rabatte = [];
    private List<TicketPosition> ticket = [];

    // Zahlung
    private ZahlungsArt zahlungsArt = ZahlungsArt.Bar;
    private decimal gegeben;
    private bool karteBestaetigt;
    private string ausgewaehlteRabattId = string.Empty;
    private bool bedingungBestaetigt;
    private string? verkaufFehler;
    private Guid? letzterVerkaufId;
    private string scanCode = string.Empty;

    // Abschluss
    private bool abschlussOffen;
    private decimal endbestandGezaehlt;
    private string? abschlussNotiz;
    private string? abschlussFehler;

    private Rabatt? ausgewaehlterRabatt =>
        Guid.TryParse(ausgewaehlteRabattId, out var id) ? rabatte.FirstOrDefault(r => r.Id == id) : null;

    private decimal zwischensumme => ticket.Sum(p => p.Leistung.Preis * p.Menge);
    private decimal rabattSumme => BerechneRabattSumme();
    private decimal gesamtSumme => Math.Max(zwischensumme - rabattSumme, 0m);
    private decimal rueckgeld => zahlungsArt == ZahlungsArt.Bar && gegeben >= gesamtSumme
        ? Math.Round(gegeben - gesamtSumme, 2)
        : 0m;

    protected override async Task OnInitializedAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return;
        }

        // Offene Sitzung eines der dem Nutzer zugeordneten Salons wiederherstellen.
        var zuordnungen = await katalogService.GetUserSalonsAsync(userId);
        foreach (var z in zuordnungen)
        {
            var offene = await kassenService.GetOffeneSitzungAsync(z.SalonId);
            if (offene is not null)
            {
                aktiveSitzung = offene;
                aktiverSalonName = z.Salon.Name;
                anzeigeState.SitzungGestartet(offene.Id, offene.SalonId, offene.Kassierer.FullName);
                await LadeKatalogAsync(offene.SalonId);
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
        anzeigeState.SitzungGestartet(sitzung.Id, sitzung.SalonId, zuordnung.AppUser.FullName);
        await LadeKatalogAsync(sitzung.SalonId);

        pin = string.Empty;
        toast.ZeigeErfolg($"Caisse ouverte – {zuordnung.Salon.Name}");
    }

    private async Task LadeKatalogAsync(Guid salonId)
    {
        leistungen = (await katalogService.GetLeistungenAsync(salonId)).Where(l => l.IstAktiv).ToList();
        rabatte = (await katalogService.GetRabatteAsync(salonId)).Where(r => r.IstAktiv).ToList();
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
    }

    private void EntfernePos(TicketPosition pos) => ticket.Remove(pos);

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
            toast.ZeigeInfo($"{treffer.Name} ajouté.");
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
        if (rabatt is null)
        {
            return 0m;
        }

        // Leistungsbezogener Rabatt gilt nur auf Positionen dieser Leistung,
        // globaler Rabatt auf den ganzen Bon.
        var basis = rabatt.LeistungId is Guid leistungId
            ? ticket.Where(p => p.Leistung.Id == leistungId).Sum(p => p.Leistung.Preis * p.Menge)
            : zwischensumme;

        var nachRabatt = Core.Services.Kasse.KassenRechner.WendeRabattAn(basis, rabatt.Art, rabatt.Wert);
        return Math.Round(basis - nachRabatt, 2);
    }

    private decimal ZeilenSumme(TicketPosition pos)
    {
        var rabatt = ausgewaehlterRabatt;
        if (rabatt is not null && (rabatt.LeistungId is null || rabatt.LeistungId == pos.Leistung.Id))
        {
            var betrag = pos.Leistung.Preis * pos.Menge;
            // Bei globalem Rabatt verteilen wir nicht pro Zeile in der Anzeige.
            if (rabatt.LeistungId is not null)
            {
                return Core.Services.Kasse.KassenRechner.WendeRabattAn(betrag, rabatt.Art, rabatt.Wert);
            }
        }

        return pos.Leistung.Preis * pos.Menge;
    }

    private async Task BucheVerkaufAsync()
    {
        verkaufFehler = null;

        if (aktiveSitzung is null)
        {
            verkaufFehler = "Aucune caisse ouverte.";
            return;
        }

        var rabatt = ausgewaehlterRabatt;
        if (rabatt is not null && !string.IsNullOrWhiteSpace(rabatt.Bedingung) && !bedingungBestaetigt)
        {
            verkaufFehler = "Veuillez confirmer la condition de la remise.";
            return;
        }

        var positionen = ticket.Select(p => new KassenService.VerkaufsPositionInput(
            p.Leistung.Id,
            p.Menge,
            rabatt is not null && (rabatt.LeistungId is null || rabatt.LeistungId == p.Leistung.Id) ? rabatt.Id : null,
            bedingungBestaetigt)).ToList();

        var (erfolg, fehler, verkauf, warnungen) = await kassenService.BucheVerkaufAsync(
            aktiveSitzung.Id, positionen, zahlungsArt,
            zahlungsArt == ZahlungsArt.Bar ? gegeben : null,
            karteBestaetigt);

        if (!erfolg || verkauf is null)
        {
            verkaufFehler = fehler;
            return;
        }

        anzeigeState.VerkaufGebucht(verkauf.GesamtSumme);
        letzterVerkaufId = verkauf.Id;
        foreach (var warnung in warnungen)
        {
            toast.ZeigeInfo(warnung);
        }

        toast.ZeigeErfolg(zahlungsArt == ZahlungsArt.Bar
            ? $"Encaissé. Monnaie à rendre : {verkauf.Rueckgeld:N0} F"
            : "Paiement par carte confirmé.");

        // Ticket zurücksetzen.
        ticket.Clear();
        gegeben = 0m;
        karteBestaetigt = false;
        ausgewaehlteRabattId = string.Empty;
        bedingungBestaetigt = false;
        zahlungsArt = ZahlungsArt.Bar;

        await LadeKatalogAsync(aktiveSitzung.SalonId);
    }

    private void OeffneAbschluss()
    {
        abschlussFehler = null;
        endbestandGezaehlt = 0m;
        abschlussNotiz = null;
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

    private sealed class TicketPosition
    {
        public Leistung Leistung { get; set; } = null!;
        public int Menge { get; set; }
        public Rabatt? Rabatt { get; set; }
    }

    private static string RabattText(Rabatt r)
        => r.Art == RabattArt.Prozent ? $"-{r.Wert:N0} %" : $"-{r.Wert:N0} F";
}
