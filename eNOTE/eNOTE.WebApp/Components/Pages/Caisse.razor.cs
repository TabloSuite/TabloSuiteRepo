using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Caisse(
    KassenService kassenService,
    BestandsService bestandsService,
    StornoService stornoService,
    KatalogService katalogService,
    MobileMoneyService mobileMoneyService,
    GeraetFavoritService geraetFavoritService,
    KassenAnzeigeState anzeigeState,
    IDisplayBenachrichtigungService displayService,
    ToastService toast,
    AuthenticationStateProvider authStateProvider,
    NavigationManager navigationManager,
    FinanzService finanzService,
    IJSRuntime js) : IDisposable
{
    private enum ZahlModus { Bar, Karte, MobileMoney, Mixte }

    private sealed record AbgeschlossenerVerkauf(Guid VerkaufId, decimal Total, decimal? Rueckgeld, string ZahlungText);

    /// <summary>Kategoriefarben (gut unterscheidbar, auf Weiß lesbar).</summary>
    private static readonly string[] KategoriePalette =
        ["#2547b8", "#0f8f86", "#c2410c", "#7c3aed", "#be185d", "#0369a1", "#4d7c0f", "#b45309"];

    // --- Start ---
    private List<(Guid UserId, string Name)> kassiererListe = [];
    private Guid ausgewaehlterKassiererId = Guid.Empty;
    private string pin = string.Empty;
    private decimal startbestand;
    private string? startFehler;
    private static readonly IReadOnlyList<decimal> startVorschlaege = [0m, 5000m, 10000m, 20000m, 50000m];

    /// <summary>Abo unbezahlt nach Frist: keine neuen Verkäufe/Starts (serverseitig ebenfalls geprüft), Abschluss möglich.</summary>
    private bool kasseGesperrt;

    // --- Aktive Kasse ---
    private KassenSitzung? aktiveSitzung;
    private string aktiverSalonName = string.Empty;
    private bool wiederhergestelltModalOffen;
    private string? wiederhergestelltKassierer;
    private decimal? wiederhergestelltSoll;
    private DateTime wiederhergestelltGestartet;

    // --- Katalog ---
    private List<Leistung> leistungen = [];
    private List<LeistungKategorie> kategorien = [];
    private Dictionary<Guid, string> bildUrls = [];
    private HashSet<Guid> favoritenIds = [];
    private Guid? aktiveKategorieId;
    private Guid? aktiveUnterKategorieId;
    private bool nurFavoriten;
    private string suchtext = string.Empty;
    private Guid aktuellerBenutzerId;

    // --- Ticket ---
    private List<TicketPosition> ticket = [];
    private bool ticketAufgeklappt;
    private AbgeschlossenerVerkauf? letzterVerkauf;
    private List<Rabatt> rabatte = [];
    private Rabatt? ausgewaehlterRabatt;
    private Rabatt? rabattKandidat;
    private bool bedingungBestaetigt;
    private bool rabattDialogOffen;
    private bool abbrechenBestaetigungOffen;

    // --- Divers (freier Betrag) ---
    private bool freierBetragOffen;
    private string? freierBetragFehler;
    private decimal freierBetrag;
    private int freierBetragMenge = 1;
    private Leistung? standardArtikel;

    // --- Zahlung ---
    private List<MobileMoneyAnbieter> mobileAnbieter = [];
    private bool zahlDialogOffen;
    private ZahlModus zahlModus = ZahlModus.Bar;
    private decimal gegeben;
    private Guid? mobileAnbieterId;
    private string? mobileReferenz;
    private decimal mixBar;
    private ZahlungsArt mixZweitArt = ZahlungsArt.MobileMoney;
    private string? verkaufFehler;

    // --- Rückerstattung ---
    private bool stornoOffen;
    private StornoService.StornoVorlage? stornoVorlage;
    private Dictionary<Guid, int> stornoMengen = [];
    private string? stornoWeg;
    private bool stornoInsLager = true;
    private string? stornoGrund;
    private string stornoPin = string.Empty;
    private string? stornoFehler;

    private StornoService.StornoWeg? StornoGewaehlterWeg => stornoVorlage?.Wege.FirstOrDefault(w => w.Schluessel == stornoWeg);

    private bool StornoMitLager => stornoVorlage?.Positionen.Any(p => p.HatLager && stornoMengen.GetValueOrDefault(p.PositionId) > 0) == true;

    /// <summary>Vorschau mit derselben Regel wie der Server (anteilig inkl. Rabatt, Rest bei letzter Menge).</summary>
    private decimal StornoBetrag => stornoVorlage is null ? 0m : stornoVorlage.Positionen
        .Where(p => stornoMengen.GetValueOrDefault(p.PositionId) is var m && m > 0 && m <= p.Offen)
        .Sum(p => Core.Services.Kasse.StornoRechner.Betrag(p.ZeilenSumme, p.Menge, p.ErstattetMenge, p.ErstattetBetrag, stornoMengen[p.PositionId]));

    private async Task OeffneStornoAsync(Guid verkaufId)
    {
        stornoVorlage = null;
        stornoFehler = null;
        stornoMengen = [];
        stornoGrund = null;
        stornoPin = string.Empty;
        stornoInsLager = true;
        historieOffen = false;
        stornoOffen = true;

        stornoVorlage = await stornoService.GetVorlageAsync(verkaufId);
        if (stornoVorlage is null)
        {
            stornoFehler = "Vente introuvable.";
            return;
        }

        StornoAlles();
        stornoWeg = stornoVorlage.Wege.Where(w => w.Offen > 0).OrderByDescending(w => w.Offen).FirstOrDefault()?.Schluessel;
    }

    private void StornoAlles()
    {
        if (stornoVorlage is null) return;
        stornoMengen = stornoVorlage.Positionen.Where(p => p.Offen > 0).ToDictionary(p => p.PositionId, p => p.Offen);
    }

    private void SetzeStornoMenge(StornoService.StornoPosition p, int menge)
        => stornoMengen[p.PositionId] = Math.Clamp(menge, 0, p.Offen);

    private async Task BestaetigeStornoAsync()
    {
        if (aktiveSitzung is null || stornoVorlage is null || stornoWeg is null) return;
        stornoFehler = null;
        var warBar = StornoGewaehlterWeg is { Art: ZahlungsArt.Bar };

        var eingaben = stornoMengen.Where(kv => kv.Value > 0).Select(kv => new StornoService.StornoPositionInput(kv.Key, kv.Value)).ToList();
        var ergebnis = await stornoService.ErstatteAsync(aktuellerBenutzerId, aktiveSitzung.Id, stornoVorlage.VerkaufId, eingaben,
            stornoWeg, stornoInsLager, stornoGrund, stornoPin);
        if (!ergebnis.Erfolg)
        {
            stornoFehler = ergebnis.Fehler;
            stornoPin = string.Empty;
            return;
        }

        if (warBar)
        {
            anzeigeState.BargeldBewegt(-ergebnis.Betrag);
        }

        stornoOffen = false;
        toast.ZeigeErfolg($"Remboursement de {ergebnis.Betrag:N0} F enregistré.");
        leistungen = (await katalogService.GetLeistungenAsync(aktiveSitzung.SalonId)).Where(l => l.IstAktiv).ToList();
        if (ergebnis.Storno is { } storno)
        {
            await DruckeBonAsync(storno.Id);
        }
    }

    // --- Entnahme durch den Besitzer ---
    private bool retraitOffen;
    private bool retraitWare;
    private bool retraitBrauchtPin;
    private decimal retraitBetrag;
    private string retraitLeistungId = string.Empty;
    private decimal retraitMenge = 1;
    private string? retraitNotiz;
    private string retraitPin = string.Empty;
    private string? retraitFehler;
    private static readonly IReadOnlyList<decimal> retraitVorschlaege = [1000m, 2000m, 5000m, 10000m];

    private IReadOnlyList<Shared.EnoteSelectOption<string>> RetraitArtikelOptionen => leistungen
        .Where(l => l.HatStueckzahl && l.Stueckzahl > 0 && !l.IstStandardArtikel)
        .OrderBy(l => l.Name)
        .Select(l => new Shared.EnoteSelectOption<string> { Value = l.Id.ToString(), Label = $"{l.Name} ({l.Stueckzahl:0.##})" })
        .ToList();

    private Leistung? RetraitArtikel => Guid.TryParse(retraitLeistungId, out var id) ? leistungen.FirstOrDefault(l => l.Id == id) : null;

    private async Task OeffneRetrait()
    {
        if (aktiveSitzung is null) return;
        retraitWare = false;
        retraitBetrag = 0m;
        retraitLeistungId = string.Empty;
        retraitMenge = 1;
        retraitNotiz = null;
        retraitPin = string.Empty;
        retraitFehler = null;
        retraitBrauchtPin = await kassenService.HatBesitzerPinAsync(aktiveSitzung.SalonId);
        retraitOffen = true;
    }

    private async Task BestaetigeRetraitAsync()
    {
        if (aktiveSitzung is null) return;
        retraitFehler = null;

        if (retraitWare)
        {
            if (RetraitArtikel is not { } artikel) return;
            var ergebnis = await bestandsService.EntnehmeWareAsync(aktuellerBenutzerId, aktiveSitzung.Id, retraitPin, artikel.Id, retraitMenge, retraitNotiz);
            if (!ergebnis.Erfolg) { retraitFehler = ergebnis.Fehler; retraitPin = string.Empty; return; }
            toast.ZeigeInfo($"Retrait enregistré : {retraitMenge:0.##} × « {artikel.Name} ».");
            leistungen = (await katalogService.GetLeistungenAsync(aktiveSitzung.SalonId)).Where(l => l.IstAktiv).ToList();
        }
        else
        {
            var ergebnis = await bestandsService.EntnehmeBargeldAsync(aktuellerBenutzerId, aktiveSitzung.Id, retraitPin, retraitBetrag, retraitNotiz);
            if (!ergebnis.Erfolg) { retraitFehler = ergebnis.Fehler; retraitPin = string.Empty; return; }
            anzeigeState.BargeldBewegt(-retraitBetrag);
            toast.ZeigeInfo($"Retrait de {retraitBetrag:N0} F enregistré.");
        }

        retraitOffen = false;
    }

    // --- Dialoge ---
    private bool displayDialogOffen;
    private string? displayQrDataUrl;
    private string displayLink = string.Empty;
    private bool historieOffen;
    private bool historieLaedt;
    private List<FinanzService.VerkaufEintrag> historieVerkaeufe = [];
    private bool abschlussOffen;
    private decimal endbestandGezaehlt;
    private string? abschlussNotiz;
    private string? abschlussFehler;
    private KassenService.SitzungsUebersicht? abschlussInfo;

    // ================= Berechnete Werte =================

    private decimal zwischensumme => ticket.Sum(p => p.EffektivPreis * p.Menge);
    private decimal rabattSumme => BerechneRabattSumme();
    private decimal gesamtSumme => Math.Max(zwischensumme - rabattSumme, 0m);
    private int AnzahlArtikel => ticket.Sum(p => p.Menge);
    private decimal Ecart => abschlussInfo is null ? 0m : endbestandGezaehlt - abschlussInfo.BarSoll;

    private IReadOnlyList<LeistungKategorie> HauptKategorien
    {
        get
        {
            var genutzt = leistungen.Where(l => l.KategorieId is not null).Select(l => l.KategorieId!.Value).ToHashSet();
            return kategorien
                .Where(k => k.ParentId is null && (genutzt.Contains(k.Id) || kategorien.Any(u => u.ParentId == k.Id && genutzt.Contains(u.Id))))
                .OrderBy(k => k.Name)
                .ToList();
        }
    }

    private IReadOnlyList<LeistungKategorie> UnterKategorien
        => aktiveKategorieId is Guid id
            ? kategorien.Where(k => k.ParentId == id && leistungen.Any(l => l.KategorieId == k.Id)).OrderBy(k => k.Name).ToList()
            : [];

    private bool IstAlles => aktiveKategorieId is null && !nurFavoriten;

    private IEnumerable<Leistung> SichtbareLeistungen
    {
        get
        {
            IEnumerable<Leistung> q = leistungen;
            var suche = suchtext.Trim();
            if (suche.Length > 0)
            {
                q = q.Where(l => l.Name.Contains(suche, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(l.StrichCode, suche, StringComparison.OrdinalIgnoreCase));
            }
            else if (nurFavoriten)
            {
                q = q.Where(l => favoritenIds.Contains(l.Id));
            }
            else if (aktiveKategorieId is Guid hauptId)
            {
                var ids = aktiveUnterKategorieId is Guid unterId
                    ? [unterId]
                    : kategorien.Where(k => k.Id == hauptId || k.ParentId == hauptId).Select(k => k.Id).ToHashSet();
                q = q.Where(l => l.KategorieId is Guid k && ids.Contains(k));
            }

            return q.OrderByDescending(l => l.IstStandardArtikel)
                .ThenByDescending(l => favoritenIds.Contains(l.Id))
                .ThenBy(l => l.Name);
        }
    }

    private string ZahlTitel => zahlModus switch
    {
        ZahlModus.Bar => "Paiement en espèces",
        ZahlModus.Karte => "Paiement par carte",
        ZahlModus.MobileMoney => "Paiement Mobile Money",
        _ => "Paiement mixte"
    };

    private string ZahlButtonText => zahlModus switch
    {
        ZahlModus.Bar => "Encaisser",
        ZahlModus.Karte => "Paiement reçu",
        ZahlModus.MobileMoney => "Paiement reçu",
        _ => "Encaisser"
    };

    private bool ZahlungMoeglich => zahlModus switch
    {
        ZahlModus.Bar => gegeben >= gesamtSumme && gesamtSumme > 0,
        ZahlModus.Karte => gesamtSumme > 0,
        ZahlModus.MobileMoney => mobileAnbieterId is not null && gesamtSumme > 0,
        _ => mixBar > 0 && mixBar < gesamtSumme && (mixZweitArt != ZahlungsArt.MobileMoney || mobileAnbieterId is not null)
    };

    private bool RabattAnwendbar => rabattKandidat is not null
        && (string.IsNullOrWhiteSpace(rabattKandidat.Bedingung) || bedingungBestaetigt);

    // ================= Lebenszyklus =================

    protected override async Task OnInitializedAsync()
    {
        anzeigeState.OnChange += HandleAnzeigeStateChanged;

        var authState = await authStateProvider.GetAuthenticationStateAsync();
        if (!Guid.TryParse(authState.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return;
        }

        aktuellerBenutzerId = userId;

        // Offene Sitzung eines der dem Nutzer zugeordneten Läden wiederherstellen.
        var zuordnungen = await katalogService.GetUserSalonsAsync(userId);
        foreach (var z in zuordnungen)
        {
            var offene = await kassenService.GetOffeneSitzungAsync(z.SalonId);
            if (offene is null)
            {
                continue;
            }

            aktiveSitzung = offene;
            aktiverSalonName = z.Salon.Name;
            var aufstellung = await kassenService.GetBarSollAufstellungAsync(offene.Id);
            anzeigeState.SitzungGestartet(offene.Id, offene.SalonId, offene.Kassierer.FullName, offene.Startbestand,
                aufstellung is { } a ? a.Soll - a.Startbestand : 0m);
            await LadeKatalogAsync(offene.SalonId);
            wiederhergestelltKassierer = offene.Kassierer.FullName;
            wiederhergestelltSoll = aufstellung?.Soll;
            wiederhergestelltGestartet = offene.GestartetUtc;
            wiederhergestelltModalOffen = true;
            return;
        }

        await LadeKassiererListeAsync(zuordnungen);
    }

    private async Task LadeKassiererListeAsync(List<Core.DataModels.Salons.UserSalon> zuordnungen)
    {
        // Kassierer aus allen dem Nutzer zugeordneten (freigegebenen) Läden sammeln.
        var distinct = new Dictionary<Guid, string>();
        foreach (var z in zuordnungen.Where(x => x.Salon.IstFreigegeben))
        {
            foreach (var k in await kassenService.GetKassiererAsync(z.SalonId))
            {
                distinct.TryAdd(k.UserId, k.Name);
            }
        }

        kassiererListe = distinct.Select(kv => (kv.Key, kv.Value)).OrderBy(x => x.Value).ToList();

        // Optionaler Geräte-Favorit: Laden vorbestimmen und dessen Kassierer zuerst zeigen.
        try
        {
            if (await geraetFavoritService.GetFavoritSalonIdAsync() is Guid salonId)
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

        // Nur ein Kassierer: direkt vorauswählen (spart einen Tipp).
        if (kassiererListe.Count == 1)
        {
            ausgewaehlterKassiererId = kassiererListe[0].UserId;
        }
    }

    private void WaehleKassierer(Guid id)
    {
        ausgewaehlterKassiererId = id;
        startFehler = null;
        pin = string.Empty;
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
            pin = string.Empty;
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
        kategorien = await katalogService.GetKategorienAsync(salonId);
        rabatte = (await katalogService.GetRabatteAsync(salonId)).Where(r => r.IstAktiv).ToList();
        favoritenIds = await katalogService.GetFavoritenIdsAsync(salonId);
        bildUrls = await katalogService.GetLeistungsBildUrlsAsync(salonId);
        mobileAnbieter = await mobileMoneyService.GetErlaubteAnbieterAsync(salonId);
    }

    // ================= Katalog-Bedienung =================

    private void ZeigeAlles()
    {
        aktiveKategorieId = null;
        aktiveUnterKategorieId = null;
        nurFavoriten = false;
        suchtext = string.Empty;
    }

    private void ZeigeFavoriten()
    {
        ZeigeAlles();
        nurFavoriten = true;
    }

    private void WaehleKategorie(Guid id)
    {
        ZeigeAlles();
        aktiveKategorieId = id;
    }

    private string KategorieFarbe(Guid? kategorieId)
    {
        if (kategorieId is not Guid id)
        {
            return "var(--enote-border-strong)";
        }

        // Unterkategorien erben die Farbe ihrer Hauptkategorie.
        var kat = kategorien.FirstOrDefault(k => k.Id == id);
        var hauptId = kat?.ParentId ?? id;
        var index = kategorien.Where(k => k.ParentId is null).OrderBy(k => k.Name).Select(k => k.Id).ToList().IndexOf(hauptId);
        return index < 0 ? "var(--enote-border-strong)" : KategoriePalette[index % KategoriePalette.Length];
    }

    private int MengeImTicket(Guid leistungId) => ticket.Where(p => p.Leistung.Id == leistungId).Sum(p => p.Menge);

    /// <summary>Standard-Artikel öffnet den Betrag-Dialog; normale Leistungen werden direkt hinzugefügt.</summary>
    private void WaehleLeistung(Leistung leistung)
    {
        if (leistung.IstStandardArtikel)
        {
            standardArtikel = leistung;
            freierBetrag = 0m;
            freierBetragMenge = 1;
            freierBetragFehler = null;
            freierBetragOffen = true;
            return;
        }

        FuegeHinzu(leistung);
    }

    private void FuegeHinzu(Leistung leistung)
    {
        letzterVerkauf = null;
        var vorhanden = ticket.FirstOrDefault(p => p.Leistung.Id == leistung.Id && p.FreierBetrag is null);
        if (vorhanden is not null)
        {
            vorhanden.Menge++;
        }
        else
        {
            ticket.Add(new TicketPosition { Leistung = leistung, Menge = 1 });
        }

        _ = SendeTicketAnDisplayAsync();
    }

    private void FuegeStandardArtikelHinzu()
    {
        if (standardArtikel is null)
        {
            return;
        }

        if (freierBetrag <= 0)
        {
            freierBetragFehler = "Veuillez saisir un montant.";
            return;
        }

        letzterVerkauf = null;
        ticket.Add(new TicketPosition { Leistung = standardArtikel, Menge = freierBetragMenge, FreierBetrag = freierBetrag });
        freierBetragOffen = false;
        _ = SendeTicketAnDisplayAsync();
    }

    private void HandleSuchTaste(KeyboardEventArgs e)
    {
        if (e.Key != "Enter" || string.IsNullOrWhiteSpace(suchtext))
        {
            return;
        }

        var code = suchtext.Trim();
        var treffer = leistungen.FirstOrDefault(l => string.Equals(l.StrichCode, code, StringComparison.OrdinalIgnoreCase));
        var sichtbar = SichtbareLeistungen.Where(l => !l.IstStandardArtikel).Take(2).ToList();
        treffer ??= sichtbar.Count == 1 ? sichtbar[0] : null;

        if (treffer is not null)
        {
            FuegeHinzu(treffer);
            suchtext = string.Empty;
        }
        else
        {
            toast.ZeigeFehler($"Aucun article unique pour « {code} ».");
        }
    }

    private async Task ToggleFavoritAsync(Guid leistungId)
    {
        if (aktiveSitzung is null || aktuellerBenutzerId == Guid.Empty)
        {
            return;
        }

        await katalogService.ToggleFavoritAsync(aktiveSitzung.SalonId, leistungId, aktuellerBenutzerId);
        favoritenIds = await katalogService.GetFavoritenIdsAsync(aktiveSitzung.SalonId);
        if (nurFavoriten && favoritenIds.Count == 0)
        {
            ZeigeAlles();
        }
    }

    // ================= Ticket =================

    private void AendereMenge(TicketPosition pos, int delta)
    {
        pos.Menge += delta;
        if (pos.Menge <= 0)
        {
            ticket.Remove(pos);
        }

        PruefeRabattNochGueltig();
        _ = SendeTicketAnDisplayAsync();
    }

    private void OeffneAbbrechenBestaetigung()
    {
        if (ticket.Count > 0)
        {
            abbrechenBestaetigungOffen = true;
        }
    }

    /// <summary>Leert das Ticket und setzt Rabatt/Eingaben zurück.</summary>
    private void BrecheTicketAb()
    {
        SetzeTicketZurueck();
        abbrechenBestaetigungOffen = false;
        _ = SendeTicketAnDisplayAsync();
    }

    private void SetzeTicketZurueck()
    {
        ticket.Clear();
        ausgewaehlterRabatt = null;
        rabattKandidat = null;
        bedingungBestaetigt = false;
        gegeben = 0m;
        mixBar = 0m;
        mobileReferenz = null;
        ticketAufgeklappt = false;
    }

    private void WendeRabattAn()
    {
        if (!RabattAnwendbar)
        {
            return;
        }

        ausgewaehlterRabatt = rabattKandidat;
        rabattDialogOffen = false;
        _ = SendeTicketAnDisplayAsync();
    }

    private void EntferneRabatt()
    {
        ausgewaehlterRabatt = null;
        rabattKandidat = null;
        bedingungBestaetigt = false;
        _ = SendeTicketAnDisplayAsync();
    }

    /// <summary>Ein leistungsbezogener Rabatt verfällt, wenn die Leistung nicht mehr im Ticket ist.</summary>
    private void PruefeRabattNochGueltig()
    {
        if (ausgewaehlterRabatt?.LeistungId is Guid id && ticket.All(p => p.Leistung.Id != id))
        {
            EntferneRabatt();
        }
    }

    private decimal BerechneRabattSumme()
    {
        var rabatt = ausgewaehlterRabatt;
        if (rabatt is null)
        {
            return 0m;
        }

        // Leistungsbezogener Rabatt gilt nur auf Positionen dieser Leistung, globaler auf den ganzen Bon.
        var basis = rabatt.LeistungId is Guid leistungId
            ? ticket.Where(p => p.Leistung.Id == leistungId).Sum(p => p.EffektivPreis * p.Menge)
            : zwischensumme;

        var nachRabatt = Core.Services.Kasse.KassenRechner.WendeRabattAn(basis, rabatt.Art, rabatt.Wert);
        return Math.Round(basis - nachRabatt, 2);
    }

    private decimal ZeilenSumme(TicketPosition pos)
    {
        var betrag = pos.EffektivPreis * pos.Menge;
        var rabatt = ausgewaehlterRabatt;
        return rabatt?.LeistungId is Guid id && id == pos.Leistung.Id
            ? Core.Services.Kasse.KassenRechner.WendeRabattAn(betrag, rabatt.Art, rabatt.Wert)
            : betrag;
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

    // ================= Zahlung =================

    private void OeffneZahlung(ZahlModus modus)
    {
        if (ticket.Count == 0)
        {
            return;
        }

        if (kasseGesperrt)
        {
            toast.ZeigeFehler("Caisse suspendue : abonnement impayé. La clôture reste possible.");
            return;
        }

        verkaufFehler = null;
        WechsleModus(modus);
        zahlDialogOffen = true;
    }

    private void WechsleModus(ZahlModus modus)
    {
        zahlModus = modus;
        verkaufFehler = null;
        gegeben = gesamtSumme; // passender Betrag vorbefüllt
        mixBar = 0m;
        mixZweitArt = mobileAnbieter.Count > 0 ? ZahlungsArt.MobileMoney : ZahlungsArt.Karte;
        mobileAnbieterId ??= mobileAnbieter.Count == 1 ? mobileAnbieter[0].Id : null;
    }

    private void SchliesseZahlung()
    {
        zahlDialogOffen = false;
        verkaufFehler = null;
    }

    private async Task BestaetigeZahlungAsync()
    {
        if (!ZahlungMoeglich || aktiveSitzung is null)
        {
            return;
        }

        verkaufFehler = null;
        IReadOnlyList<KassenService.ZahlungInput> zahlungen = zahlModus switch
        {
            ZahlModus.Bar => [new(ZahlungsArt.Bar, Gegeben: gegeben)],
            ZahlModus.Karte => [new(ZahlungsArt.Karte)],
            ZahlModus.MobileMoney => [new(ZahlungsArt.MobileMoney, AnbieterId: mobileAnbieterId, Referenz: mobileReferenz)],
            _ =>
            [
                new(ZahlungsArt.Bar, Betrag: mixBar, Gegeben: mixBar),
                new(mixZweitArt, AnbieterId: mixZweitArt == ZahlungsArt.MobileMoney ? mobileAnbieterId : null)
            ]
        };

        var rabatt = ausgewaehlterRabatt;
        var positionen = ticket.Select(p => new KassenService.VerkaufsPositionInput(
            p.Leistung.Id,
            p.Menge,
            rabatt is not null && (rabatt.LeistungId is null || rabatt.LeistungId == p.Leistung.Id) ? rabatt.Id : null,
            bedingungBestaetigt,
            p.FreierBetrag)).ToList();

        var (erfolg, fehler, verkauf, warnungen) = await kassenService.BucheVerkaufAsync(aktiveSitzung.Id, positionen, zahlungen);
        if (!erfolg || verkauf is null)
        {
            verkaufFehler = fehler;
            return;
        }

        var anteile = Core.Services.Kasse.KassenRechner.Anteile(verkauf);
        anzeigeState.VerkaufGebucht(anteile.Where(a => a.Art == ZahlungsArt.Bar).Sum(a => a.Betrag));
        letzterVerkauf = new AbgeschlossenerVerkauf(verkauf.Id, verkauf.GesamtSumme, verkauf.Rueckgeld, ZahlungsTexte.Kurz(anteile));
        zahlDialogOffen = false;

        foreach (var warnung in warnungen)
        {
            toast.ZeigeInfo(warnung);
        }

        // Kunden-Display: abgeschlossenen Verkauf zeigen (Total + Rückgeld).
        var abgeschlossenesTicket = new DisplayTicket(
            verkauf.Positionen.Select(p => new DisplayPosition(p.LeistungName, p.Menge, p.ZeilenSumme)).ToList(),
            verkauf.Zwischensumme, verkauf.RabattSumme, verkauf.GesamtSumme,
            Abgeschlossen: true, Rueckgeld: verkauf.Rueckgeld);
        _ = displayService.TicketAktualisiertAsync(aktiveSitzung.Id, abgeschlossenesTicket);

        SetzeTicketZurueck();
        ticketAufgeklappt = true; // auf Handy/Tablet das Rückgeld sofort zeigen

        // Bestand (Lagerwarnungen) aktualisieren.
        leistungen = (await katalogService.GetLeistungenAsync(aktiveSitzung.SalonId)).Where(l => l.IstAktiv).ToList();
    }

    // ================= Dialoge =================

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
        abschlussInfo = aktiveSitzung is not null ? await kassenService.GetSitzungsUebersichtAsync(aktiveSitzung.Id) : null;
        abschlussOffen = true;
    }

    private async Task SchliesseSitzungAsync()
    {
        if (aktiveSitzung is null)
        {
            return;
        }

        var (erfolg, fehler, sitzung) = await kassenService.SchliesseSitzungAsync(aktiveSitzung.Id, endbestandGezaehlt, abschlussNotiz);
        if (!erfolg || sitzung is null)
        {
            abschlussFehler = fehler;
            return;
        }

        abschlussOffen = false;
        var differenzText = sitzung.Differenz >= 0 ? $"+{sitzung.Differenz:N0}" : $"{sitzung.Differenz:N0}";
        toast.ZeigeErfolg($"Caisse clôturée · écart {differenzText} F.");

        aktiveSitzung = null;
        letzterVerkauf = null;
        SetzeTicketZurueck();
        anzeigeState.SitzungBeendet();

        // Zurück zum Start: Kassiererliste neu laden.
        if (aktuellerBenutzerId != Guid.Empty)
        {
            ausgewaehlterKassiererId = Guid.Empty;
            startbestand = 0m;
            await LadeKassiererListeAsync(await katalogService.GetUserSalonsAsync(aktuellerBenutzerId));
        }
    }

    /// <summary>Öffnet den Browserdruck; im eingerichteten Windows-Kioskprofil ohne Druckdialog.</summary>
    private async Task DruckeBonAsync(Guid verkaufId)
        => await js.InvokeVoidAsync("enoteOeffneTab", $"/bon/{verkaufId}/druck");

    private void HandleAnzeigeStateChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => anzeigeState.OnChange -= HandleAnzeigeStateChanged;

    // ================= Hilfen =================

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

    /// <summary>Lagermenge ohne überflüssige Nachkommastellen (3 statt 3,00).</summary>
    private static string Menge(decimal wert) => wert.ToString("0.##");

    private static string RabattText(Rabatt r)
        => r.Art == RabattArt.Prozent ? $"-{r.Wert:0.##} %" : $"-{r.Wert:N0} F";

    private sealed class TicketPosition
    {
        public Leistung Leistung { get; set; } = null!;
        public int Menge { get; set; }

        /// <summary>Freier Betrag beim Standard-Artikel „Divers“ (ersetzt den Preis).</summary>
        public decimal? FreierBetrag { get; set; }

        public decimal EffektivPreis => FreierBetrag ?? Leistung.Preis;
    }
}
