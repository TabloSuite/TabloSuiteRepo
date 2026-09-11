using System.Security.Claims;
using HalloApp.Core.DataModels.Finanzen;
using HalloApp.Core.Models.Billing;
using HalloApp.Core.Services;
using HalloApp.WebApp.Components.Shared;
using HalloApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace HalloApp.WebApp.Components.Pages;

public partial class Finanzen
{
    [Inject] private FinanzService FinanzService { get; set; } = default!;
    [Inject] private AbrechnungService AbrechnungService { get; set; } = default!;
    [Inject] private DatabaseActionGuard ActionGuard { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private Guid currentUserId;
    private bool istSuperAdmin;
    private bool istSchulleiter;

    // Schulkontext
    private List<FinanzAuswahl> schulen = new();
    private Guid? aktiveSchulID;

    // Niveaus
    private List<FinanzAuswahl> niveaus = new();

    // Tabs
    private string aktiverTab = "dashboard";

    // Meldungen
    private string? message;
    private string? errorMessage;

    // ---- Dashboard ----
    private FinanzUebersicht? uebersicht;
    private AbrechnungsUebersicht? halloAppAboUebersicht;
    private IReadOnlyList<Abrechnungsperiode> offeneHalloAppAbrechnungen = [];
    private DirektorKontostandUebersicht? direktorKontostand;
    private FinanzFilter filter = new();
    private List<FinanzKategorie> alleKategorien = new();

    // ---- Buchung erfassen ----
    private BuchungFormModel buchungForm = new();
    private bool kategorieAnlegenModus;
    private string neueKategorieBezeichnung = string.Empty;
    private FinanzRichtung neueKategorieRichtung = FinanzRichtung.Ausgabe;

    // ---- Durchläufe ----
    private List<DurchlaufUebersicht> durchlaeufe = new();
    private DurchlaufFormModel durchlaufForm = new();
    private string durchlaufStatusFilter = "all";
    private Guid? durchlaufKlasseFilterId;
    private string schuelerSuche = string.Empty;
    private readonly Dictionary<Guid, (string Status, Guid? KlasseId)> durchlaufFilterProSchule = new();

    // ---- Zahlung buchen ----
    private DurchlaufUebersicht? ausgewaehlterDurchlauf;
    private ZahlungFormModel zahlungForm = new();
    private string zahlungSchuelerSuche = string.Empty;

    // ---- Durchlauf beenden (Bestätigung) ----
    private DurchlaufUebersicht? beendenDurchlauf;
    private DateTime beendenDatum = DateTime.Today;
    private decimal beendenOffeneSchuld;
    private int beendenOffeneHalloAppAbos;
    private decimal beendenOffenerHalloAppUmsatz;

    private static DateTime ToInclusiveUtcDayEnd(DateTime date)
    {
        var localDayEnd = DateTime.SpecifyKind(date.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local);
        return localDayEnd.ToUniversalTime();
    }

    private bool IsBusy => ActionGuard.IsBusy;

    private string AktiveSchuleName => schulen.FirstOrDefault(schule => schule.Id == aktiveSchulID)?.Name ?? "École";

    private string BerichtZeitraumLabel => filter switch
    {
        { Von: not null, Bis: not null } => $"Du {filter.Von:dd.MM.yyyy} au {filter.Bis:dd.MM.yyyy}",
        { Von: not null } => $"À partir du {filter.Von:dd.MM.yyyy}",
        { Bis: not null } => $"Jusqu'au {filter.Bis:dd.MM.yyyy}",
        _ => "Toutes les dates"
    };

    private string BerichtTypLabel => filter.Richtung switch
    {
        FinanzRichtung.Einnahme => "Recettes uniquement",
        FinanzRichtung.Ausgabe => "Dépenses uniquement",
        _ => "Recettes et dépenses"
    };

    private string BerichtSortierungLabel => filter.Sortierung switch
    {
        FinanzSortierung.WirkungsdatumAufsteigend => "Date croissante",
        FinanzSortierung.BetragAbsteigend => "Montant décroissant",
        FinanzSortierung.BetragAufsteigend => "Montant croissant",
        FinanzSortierung.ErstelltAmAbsteigend => "Saisie récente",
        _ => "Date récente"
    };

    private static bool IstAboSystemBuchung(FinanzUebersichtZeile zeile)
        => zeile.KategorieSystemCode is FinanzSystemCode.HalloAppAboZahlung
            or FinanzSystemCode.HalloAppAboAnteil
            or FinanzSystemCode.HalloAppAboSchulleiterAnteil
            or FinanzSystemCode.HalloAppAboLehrerAnteil
            or FinanzSystemCode.HalloAppAboProductOwnerAnteil;

    private IReadOnlyList<FinanzUebersichtZeile> EchteSchulBuchungen => uebersicht?.Zeilen
        .Where(zeile => !IstAboSystemBuchung(zeile))
        .ToList()
        ?? [];

    private decimal SummeEntrees => uebersicht?.SummeEinnahmen ?? 0m;
    private decimal SummeSorties => uebersicht?.SummeAusgaben ?? 0m;
    private decimal SortiesComptables => Math.Max(0m, SummeSorties - CommissionLehrer);
    private decimal SoldeNetApresSorties => SummeEntrees - SortiesComptables;

    private decimal HalloAppAboTotal => uebersicht?.Zeilen
        .Where(zeile => zeile.KategorieSystemCode == FinanzSystemCode.HalloAppAboZahlung && zeile.Richtung == FinanzRichtung.Einnahme)
        .Sum(zeile => zeile.Betrag) ?? 0m;

    private decimal CommissionLehrer => uebersicht?.Zeilen
        .Where(zeile => zeile.KategorieSystemCode == FinanzSystemCode.HalloAppAboLehrerAnteil && zeile.Richtung == FinanzRichtung.Ausgabe)
        .Sum(zeile => zeile.Betrag) ?? 0m;

    private decimal CommissionSchulleiter => uebersicht?.Zeilen
        .Where(zeile => zeile.KategorieSystemCode == FinanzSystemCode.HalloAppAboSchulleiterAnteil && zeile.Richtung == FinanzRichtung.Ausgabe)
        .Sum(zeile => zeile.Betrag) ?? 0m;

    private decimal CommissionProductOwner => uebersicht?.Zeilen
        .Where(zeile => zeile.KategorieSystemCode == FinanzSystemCode.HalloAppAboProductOwnerAnteil && zeile.Richtung == FinanzRichtung.Ausgabe)
        .Sum(zeile => zeile.Betrag) ?? 0m;

    private decimal BetragFuerHalloAppPlattform => uebersicht?.Zeilen
        .Where(zeile => zeile.KategorieSystemCode == FinanzSystemCode.HalloAppAboAnteil && zeile.Richtung == FinanzRichtung.Ausgabe)
        .Sum(zeile => zeile.Betrag) ?? 0m;

    private decimal CommissionSchule => Math.Max(0m, HalloAppAboTotal - CommissionLehrer - CommissionProductOwner - BetragFuerHalloAppPlattform);

    private decimal TotalAvecHalloAppAbo => SummeEntrees;

    private decimal ResteApresPartHalloApp => SummeEntrees - BetragFuerHalloAppPlattform;

    private decimal EchteBuchungenEntrees => EchteSchulBuchungen
        .Where(zeile => zeile.Richtung == FinanzRichtung.Einnahme)
        .Sum(zeile => zeile.Betrag);

    private decimal EchteBuchungenSorties => EchteSchulBuchungen
        .Where(zeile => zeile.Richtung == FinanzRichtung.Ausgabe)
        .Sum(zeile => zeile.Betrag);

    private decimal EchteBuchungenSaldo => EchteBuchungenEntrees - EchteBuchungenSorties;

    private bool includeSkoulouAppSharesInPeriodSummary;

    private decimal ZeitraumSortiesBasis => EchteBuchungenSorties;

    private decimal ZeitraumSortiesMitAnteil => ZeitraumSortiesBasis + CommissionSchulleiter + CommissionLehrer;

    private decimal ZeitraumSortiesAnzeige => includeSkoulouAppSharesInPeriodSummary
        ? ZeitraumSortiesMitAnteil
        : ZeitraumSortiesBasis;

    private decimal ZeitraumSaldoAnzeige => EchteBuchungenEntrees - ZeitraumSortiesAnzeige;

    private string ZeitraumOptionLabel => includeSkoulouAppSharesInPeriodSummary
        ? "Sorties + parts SKOULOUAPP (direction + enseignants)"
        : "Sorties réelles (sans parts SKOULOUAPP)";

    private decimal SchulgebuehrenEinnahmen => EchteSchulBuchungen
        .Where(zeile => zeile.KategorieSystemCode == FinanzSystemCode.KursNiveauZahlung && zeile.Richtung == FinanzRichtung.Einnahme)
        .Sum(zeile => zeile.Betrag);

    private decimal WeitereEchteEntrees => EchteSchulBuchungen
        .Where(zeile => zeile.KategorieSystemCode != FinanzSystemCode.KursNiveauZahlung && zeile.Richtung == FinanzRichtung.Einnahme)
        .Sum(zeile => zeile.Betrag);

    private decimal SoldeApresBuchungen => SchulgebuehrenEinnahmen + WeitereEchteEntrees - EchteBuchungenSorties;

    private decimal SoldeTotalAvecAbo => SoldeApresBuchungen + HalloAppAboTotal;

    private decimal SoldeTotalOhneHalloAppPart => SoldeTotalAvecAbo - BetragFuerHalloAppPlattform;

    private decimal OffeneSchulgebuehren => direktorKontostand?.OffeneSchulgebuehrenAktiveKlassen ?? 0m;

    private decimal OffeneAboGebuehren => direktorKontostand?.OffeneAboGebuehrenAktiveKlassen ?? 0m;

    private decimal SoldeCibleAvecAbo => SoldeTotalAvecAbo + OffeneSchulgebuehren + OffeneAboGebuehren;

    private decimal SoldeCibleOhneHalloAppPart => SoldeCibleAvecAbo - BetragFuerHalloAppPlattform;

    private decimal SoldeActuelAffiche => SoldeTotalOhneHalloAppPart;

    private static string SoldeFarbe(decimal betrag)
        => betrag < 0m ? "text-rose-700" : "text-emerald-700";

    private static string GetKategorieLabel(FinanzUebersichtZeile zeile)
        => zeile.KategorieSystemCode == FinanzSystemCode.KursNiveauZahlung
            ? "Scolarité"
            : zeile.Kategorie;

    private IReadOnlyList<ModernDropdownOption<Guid?>> SchulOptionen => schulen
        .Select(schule => new ModernDropdownOption<Guid?>((Guid?)schule.Id, schule.Name))
        .ToList();

    private IReadOnlyList<ModernDropdownOption<FinanzRichtung?>> RichtungsOptionen =>
    [
        new(null, "Tous", "Recettes et dépenses"),
        new(FinanzRichtung.Einnahme, "Recettes", "Entrées d'argent"),
        new(FinanzRichtung.Ausgabe, "Dépenses", "Sorties d'argent")
    ];

    private IReadOnlyList<ModernDropdownOption<FinanzSortierung?>> SortierungsOptionen =>
    [
        new(FinanzSortierung.WirkungsdatumAbsteigend, "Date (récent)"),
        new(FinanzSortierung.WirkungsdatumAufsteigend, "Date (ancien)"),
        new(FinanzSortierung.BetragAbsteigend, "Montant (haut)"),
        new(FinanzSortierung.BetragAufsteigend, "Montant (bas)")
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> KategorieOptionen =>
    [
        new((Guid?)Guid.Empty, "— choisir —"),
        .. alleKategorien
            .Where(kategorie => kategorie.SystemCode == FinanzSystemCode.Keiner)
            .Select(kategorie => new ModernDropdownOption<Guid?>((Guid?)kategorie.Id, kategorie.Bezeichnung, kategorie.Richtung == FinanzRichtung.Einnahme ? "Recette" : "Dépense"))
    ];

    private FinanzKategorie? AusgewaehlteKategorie => alleKategorien
        .FirstOrDefault(kategorie => kategorie.Id == buchungForm.KategorieID);

    private bool KannAusgewaehlteKategorieLoeschen => AusgewaehlteKategorie is { IstSystemTyp: false, SystemCode: FinanzSystemCode.Keiner };

    private IReadOnlyList<ModernDropdownOption<FinanzRichtung?>> KategorieRichtungsOptionen =>
    [
        new(FinanzRichtung.Einnahme, "Recette"),
        new(FinanzRichtung.Ausgabe, "Dépense")
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> NiveauOptionen =>
    [
        new((Guid?)Guid.Empty, "— choisir —"),
        .. niveaus.Select(niveau => new ModernDropdownOption<Guid?>((Guid?)niveau.Id, niveau.Name))
    ];

    private IReadOnlyList<ModernDropdownOption<string?>> DurchlaufStatusOptionen =>
    [
        new("active", "En cours"),
        new("closed", "Clôturés")
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> DurchlaufKlasseOptionen =>
    [
        new(null, "Toutes les classes"),
        .. niveaus.Select(klasse => new ModernDropdownOption<Guid?>((Guid?)klasse.Id, klasse.Name))
    ];

    private IReadOnlyList<DurchlaufUebersicht> GefilterteDurchlaeufe => durchlaeufe
        .Where(durchlauf => durchlaufStatusFilter switch
        {
            "active" => durchlauf.IstAktiv,
            "closed" => !durchlauf.IstAktiv,
            _ => true
        })
        .Where(durchlauf => !durchlaufKlasseFilterId.HasValue || durchlauf.KlasseId == durchlaufKlasseFilterId.Value)
        .ToList();

    private IReadOnlyList<SchuelerZahlungsstand> GefilterteSchueler(DurchlaufUebersicht durchlauf)
        => string.IsNullOrWhiteSpace(schuelerSuche)
            ? durchlauf.Schueler
            : durchlauf.Schueler
                .Where(schueler => schueler.Name.Contains(schuelerSuche.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

    private IReadOnlyList<ModernDropdownOption<Guid?>> ZahlungsSchuelerOptionen =>
    [
        new((Guid?)Guid.Empty, "— choisir —"),
        .. (ausgewaehlterDurchlauf?.Schueler ?? [])
            .Select(schueler => new ModernDropdownOption<Guid?>((Guid?)schueler.StudentId, schueler.Name, $"Ouvert : {schueler.Offen:N0} FCFA"))
    ];

    private bool IstCashZahlung => zahlungForm.ZahlungsArt == ZahlungsArt.Cash;

    private decimal AusgewaehlterSchuelerOffen => ausgewaehlterDurchlauf?.Schueler
        .FirstOrDefault(schueler => schueler.StudentId == zahlungForm.StudentId)?.Offen ?? 0m;

    private void CashBetragAktualisieren()
    {
        if (IstCashZahlung && zahlungForm.StudentId != Guid.Empty)
        {
            zahlungForm.Betrag = AusgewaehlterSchuelerOffen;
        }
    }

    private bool HatAktivenDurchlaufFuerAusgewaehltesNiveau => durchlaufForm.KlasseId != Guid.Empty
        && durchlaeufe.Any(durchlauf => durchlauf.KlasseId == durchlaufForm.KlasseId && durchlauf.IstAktiv);

    private bool KannDurchlaufStarten => durchlaufForm.KlasseId != Guid.Empty
        && !HatAktivenDurchlaufFuerAusgewaehltesNiveau
        && !IsBusy;

    private string AusgewaehlteKlasseName => niveaus.FirstOrDefault(niveau => niveau.Id == durchlaufForm.KlasseId)?.Name ?? string.Empty;

    private string DurchlaufStartButtonClass => KannDurchlaufStarten
        ? "rounded-xl bg-emerald-600 px-5 py-2.5 text-sm font-semibold text-white transition hover:bg-emerald-700"
        : "rounded-xl cursor-not-allowed bg-slate-300 px-5 py-2.5 text-sm font-semibold text-slate-500 transition";

    private IReadOnlyList<ModernDropdownOption<ZahlungsArt?>> ZahlungsArtOptionen =>
    [
        new(ZahlungsArt.Rate, "Échelonné"),
        new(ZahlungsArt.Cash, "Cash (total)")
    ];

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            istSuperAdmin = user.IsInRole("SuperAdmin");
            istSchulleiter = user.IsInRole("SchulLeiter");

            var idValue = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid.TryParse(idValue, out currentUserId);

            schulen = await FinanzService.GetSchulenAsync(currentUserId, istSuperAdmin, ComponentToken);

            // SchulLeiter: auf die eigene(n) Schule(n) beschränken. Zur Vereinfachung
            // wird die erste zugeordnete Schule vorausgewählt; SuperAdmin darf frei wählen.
            aktiveSchulID = schulen.Count > 0 ? schulen[0].Id : (Guid?)null;

            if (aktiveSchulID.HasValue)
            {
                niveaus = await FinanzService.GetNiveausAsync(aktiveSchulID.Value, ComponentToken);
                StelleDurchlaufFilterFuerAktuelleSchuleWiederHer();
            }

            await LadeAllesAsync();
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private async Task LadeAllesAsync()
    {
        if (aktiveSchulID is null)
        {
            return;
        }

        if (!schulen.Any(school => school.Id == aktiveSchulID.Value))
        {
            aktiveSchulID = schulen.FirstOrDefault()?.Id;
            if (aktiveSchulID is null)
            {
                return;
            }
        }

        alleKategorien = await FinanzService.GetKategorienAsync(aktiveSchulID.Value);
        await LadeUebersichtAsync();
        durchlaeufe = await FinanzService.GetDurchlaeufeAsync(aktiveSchulID.Value);
        halloAppAboUebersicht = await AbrechnungService.GetOffeneUebersichtAsync(aktiveSchulID.Value, cancellationToken: ComponentToken);
        offeneHalloAppAbrechnungen = istSchulleiter
            ? (await AbrechnungService.GetHistorieAsync(aktiveSchulID.Value, ComponentToken))
                .Where(periode => periode.Status is AbrechnungStatus.Erstellt or AbrechnungStatus.Akzeptiert)
                .ToList()
            : [];
        direktorKontostand = !istSchulleiter
            ? null
            : await FinanzService.GetDirektorKontostandUebersichtAsync(aktiveSchulID.Value, filter, ComponentToken);
    }

    private async Task LadeUebersichtAsync()
    {
        if (aktiveSchulID is null)
        {
            return;
        }

        filter.SchulID = aktiveSchulID;
        uebersicht = await FinanzService.GetUebersichtAsync(filter);
        direktorKontostand = !istSchulleiter
            ? null
            : await FinanzService.GetDirektorKontostandUebersichtAsync(aktiveSchulID.Value, cancellationToken: ComponentToken);
    }

    private async Task SchuleWechselnAsync(ChangeEventArgs e)
    {
        if (Guid.TryParse(e.Value?.ToString(), out var id))
        {
            await SchuleWechselnAsync((Guid?)id);
        }
    }

    private async Task SchuleWechselnAsync(Guid? id)
    {
        if (id is null)
        {
            return;
        }

        MerkeDurchlaufFilterFuerAktuelleSchule();

        aktiveSchulID = id.Value;
        niveaus = await FinanzService.GetNiveausAsync(id.Value, ComponentToken);
        durchlaufForm = new();
        StelleDurchlaufFilterFuerAktuelleSchuleWiederHer();
        await LadeAllesAsync();
    }

    private Task SetDurchlaufStatusFilterAsync(string? value)
    {
        durchlaufStatusFilter = value ?? "active";
        MerkeDurchlaufFilterFuerAktuelleSchule();
        return Task.CompletedTask;
    }

    private Task SetDurchlaufKlasseFilterAsync(Guid? value)
    {
        durchlaufKlasseFilterId = value;
        MerkeDurchlaufFilterFuerAktuelleSchule();
        return Task.CompletedTask;
    }

    private void MerkeDurchlaufFilterFuerAktuelleSchule()
    {
        if (aktiveSchulID is Guid schoolId)
        {
            durchlaufFilterProSchule[schoolId] = (durchlaufStatusFilter, durchlaufKlasseFilterId);
        }
    }

    private void StelleDurchlaufFilterFuerAktuelleSchuleWiederHer()
    {
        if (aktiveSchulID is not Guid schoolId)
        {
            durchlaufStatusFilter = "active";
            durchlaufKlasseFilterId = null;
            return;
        }

        if (durchlaufFilterProSchule.TryGetValue(schoolId, out var zustand))
        {
            durchlaufStatusFilter = string.IsNullOrWhiteSpace(zustand.Status) ? "active" : zustand.Status;
            durchlaufKlasseFilterId = zustand.KlasseId;
            return;
        }

        durchlaufStatusFilter = "active";
        durchlaufKlasseFilterId = null;
    }

    private Task SetFilterVonAsync(DateTime? value)
    {
        filter.Von = value?.Date;
        return Task.CompletedTask;
    }

    private Task SetFilterBisAsync(DateTime? value)
    {
        filter.Bis = value?.Date;
        return Task.CompletedTask;
    }

    private Task SetFilterRichtungAsync(FinanzRichtung? value)
    {
        filter.Richtung = value;
        return Task.CompletedTask;
    }

    private Task SetFilterSortierungAsync(FinanzSortierung? value)
    {
        if (value is FinanzSortierung sortierung)
        {
            filter.Sortierung = sortierung;
        }

        return Task.CompletedTask;
    }

    private Task SetBuchungKategorieAsync(Guid? value)
    {
        buchungForm.KategorieID = value ?? Guid.Empty;
        return Task.CompletedTask;
    }

    private Task SetNeueKategorieRichtungAsync(FinanzRichtung? value)
    {
        if (value is FinanzRichtung richtung)
        {
            neueKategorieRichtung = richtung;
        }

        return Task.CompletedTask;
    }

    private Task SetBuchungWirkungsdatumAsync(DateTime? value)
    {
        if (value is DateTime datum)
        {
            buchungForm.Wirkungsdatum = datum.Date;
        }

        return Task.CompletedTask;
    }

    private void SetTab(string tab)
    {
        aktiverTab = tab;
        message = null;
        errorMessage = null;
    }

    private async Task NiveauFuerStartGewaehltAsync(Guid? levelId)
    {
        if (aktiveSchulID is not null && levelId is Guid id)
        {
            durchlaufForm.KlasseId = id;
            durchlaufForm.NiveauPreis = await FinanzService.GetPreisVorschlagAsync(aktiveSchulID.Value, id);
        }
    }

    private Task SetDurchlaufStartDatumAsync(DateTime? value)
    {
        if (value is DateTime datum)
        {
            durchlaufForm.StartDatum = datum.Date;
        }

        return Task.CompletedTask;
    }

    private Task SetZahlungStudentAsync(Guid? value)
    {
        zahlungForm.StudentId = value ?? Guid.Empty;
        CashBetragAktualisieren();
        return Task.CompletedTask;
    }

    private Task SetZahlungsArtAsync(ZahlungsArt? value)
    {
        if (value is ZahlungsArt zahlungsArt)
        {
            zahlungForm.ZahlungsArt = zahlungsArt;
            CashBetragAktualisieren();
        }

        return Task.CompletedTask;
    }

    private Task SetZahlungWirkungsdatumAsync(DateTime? value)
    {
        if (value is DateTime datum)
        {
            zahlungForm.Wirkungsdatum = datum.Date;
        }

        return Task.CompletedTask;
    }

    // -------------------- Buchung erfassen --------------------

    private async Task BuchungErfassenAsync()
    {
        if (aktiveSchulID is null)
        {
            return;
        }

        await ActionGuard.RunAsync(async () =>
        {
            var (ok, err, _) = await FinanzService.BuchungErfassenAsync(
                aktiveSchulID.Value,
                buchungForm.KategorieID,
                buchungForm.Betrag,
                buchungForm.Wirkungsdatum,
                currentUserId,
                buchungForm.BeschreibungHtml,
                istSuperAdmin,
                autorisierungErzwingen: true);

            if (ok)
            {
                message = "L'écriture a été enregistrée.";
                errorMessage = null;
                buchungForm = new();
                await LadeUebersichtAsync();
            }
            else
            {
                errorMessage = err;
            }
        });
    }

    private async Task KategorieAnlegenAsync()
    {
        if (aktiveSchulID is null)
        {
            return;
        }

        await ActionGuard.RunAsync(async () =>
        {
            // SchulLeiter-Kategorien schul-lokal, SuperAdmin-Kategorien global.
            Guid? scope = istSuperAdmin ? null : aktiveSchulID;
            var (ok, err, kat) = await FinanzService.KategorieAnlegenAsync(
                scope, neueKategorieBezeichnung, neueKategorieRichtung, currentUserId, istSuperAdmin, autorisierungErzwingen: true);

            if (ok && kat is not null)
            {
                alleKategorien = await FinanzService.GetKategorienAsync(aktiveSchulID.Value);
                buchungForm.KategorieID = kat.Id;
                kategorieAnlegenModus = false;
                neueKategorieBezeichnung = string.Empty;
                message = "La catégorie a été créée.";
                errorMessage = null;
            }
            else
            {
                errorMessage = err;
            }
        });
    }

    private async Task KategorieLoeschenAsync()
    {
        if (!KannAusgewaehlteKategorieLoeschen || buchungForm.KategorieID == Guid.Empty)
        {
            return;
        }

        await ActionGuard.RunAsync(async () =>
        {
            var (ok, err) = await FinanzService.KategorieLoeschenAsync(
                buchungForm.KategorieID,
                currentUserId,
                istSuperAdmin,
                ComponentToken);
            if (ok)
            {
                alleKategorien = await FinanzService.GetKategorienAsync(aktiveSchulID!.Value, cancellationToken: ComponentToken);
                buchungForm.KategorieID = Guid.Empty;
                message = "La catégorie a été supprimée.";
                errorMessage = null;
            }
            else
            {
                errorMessage = err;
            }
        });
    }

    private async Task BuchungLoeschenAsync(Guid buchungId)
    {
        await ActionGuard.RunAsync(async () =>
        {
            var (ok, err) = await FinanzService.BuchungLoeschenAsync(buchungId, currentUserId, istSuperAdmin);
            if (ok)
            {
                message = "L'écriture a été supprimée.";
                errorMessage = null;
                await LadeUebersichtAsync();
            }
            else
            {
                errorMessage = err;
            }
        });
    }

    // -------------------- Durchläufe --------------------

    private async Task NiveauFuerStartGewaehltAsync(ChangeEventArgs e)
    {
        if (aktiveSchulID is not null && Guid.TryParse(e.Value?.ToString(), out var levelId))
        {
            durchlaufForm.KlasseId = levelId;
            durchlaufForm.NiveauPreis = await FinanzService.GetPreisVorschlagAsync(aktiveSchulID.Value, levelId);
        }
    }

    private async Task DurchlaufStartenAsync()
    {
        if (aktiveSchulID is null || durchlaufForm.KlasseId == Guid.Empty)
        {
            errorMessage = "Veuillez sélectionner une classe.";
            return;
        }

        await ActionGuard.RunAsync(async () =>
        {
            var (ok, err, _) = await FinanzService.DurchlaufStartenAsync(
                aktiveSchulID.Value,
                durchlaufForm.KlasseId,
                durchlaufForm.NiveauPreis,
                durchlaufForm.StartDatum,
                currentUserId,
                istSuperAdmin,
                autorisierungErzwingen: true);

            if (ok)
            {
                message = "Le cycle de classe a démarré.";
                errorMessage = null;
                durchlaufStartModalOpen = false;
                durchlaufForm = new();
                durchlaeufe = await FinanzService.GetDurchlaeufeAsync(aktiveSchulID.Value);
            }
            else
            {
                errorMessage = err;
            }
        });
    }

    private async Task BeendenVorbereitenAsync(DurchlaufUebersicht d)
    {
        beendenDurchlauf = d;
        beendenDatum = DateTime.Today;
        beendenOffeneSchuld = d.SummeOffen;
        await LadeOffeneHalloAppAbosFuerBeendenAsync();
    }

    private void BeendenAbbrechen() => beendenDurchlauf = null;

    private Task SetBeendenDatumAsync(DateTime? value)
    {
        if (value is DateTime datum)
        {
            beendenDatum = datum.Date;
        }

        _ = InvokeAsync(LadeOffeneHalloAppAbosFuerBeendenAsync);

        return Task.CompletedTask;
    }

    private async Task LadeOffeneHalloAppAbosFuerBeendenAsync()
    {
        if (aktiveSchulID is null || beendenDurchlauf is null)
        {
            beendenOffeneHalloAppAbos = 0;
            beendenOffenerHalloAppUmsatz = 0m;
            return;
        }

        var uebersicht = await AbrechnungService.GetOffeneUebersichtAsync(
            aktiveSchulID.Value,
            ToInclusiveUtcDayEnd(beendenDatum),
            ComponentToken);
        beendenOffeneHalloAppAbos = uebersicht.AnzahlHalloBasis + uebersicht.AnzahlHalloPlus;
        beendenOffenerHalloAppUmsatz = uebersicht.OffenerUmsatz;
    }

    private async Task DurchlaufBeendenBestaetigenAsync()
    {
        if (beendenDurchlauf is null || aktiveSchulID is null)
        {
            return;
        }

        var durchlaufId = beendenDurchlauf.DurchlaufID;
        await ActionGuard.RunAsync(async () =>
        {
            var (ok, err, offen) = await FinanzService.DurchlaufBeendenAsync(
                durchlaufId,
                beendenDatum,
                currentUserId,
                istSuperAdmin,
                autorisierungErzwingen: true,
                ComponentToken);
            if (ok)
            {
                var schliessenMessage = offen > 0
                    ? $"Le cycle est clôturé. Le solde impayé ({offen:N0} FCFA) est enregistré comme perte."
                    : "Le cycle a été clôturé.";
                message = schliessenMessage;
                errorMessage = null;
                beendenDurchlauf = null;
                beendenOffeneHalloAppAbos = 0;
                beendenOffenerHalloAppUmsatz = 0m;
                durchlaeufe = await FinanzService.GetDurchlaeufeAsync(aktiveSchulID.Value);
            }
            else
            {
                errorMessage = err;
            }
        });
    }

    // -------------------- Zahlung buchen --------------------

    private void ZahlungPanelOeffnen(DurchlaufUebersicht d)
    {
        ausgewaehlterDurchlauf = d;
        zahlungForm = new()
        {
            Betrag = d.NiveauPreis,
            Wirkungsdatum = DateTime.Today
        };
    }

    private void ZahlungPanelSchliessen()
    {
        ausgewaehlterDurchlauf = null;
    }

    private async Task ZahlungBuchenAsync()
    {
        if (ausgewaehlterDurchlauf is null || zahlungForm.StudentId == Guid.Empty)
        {
            errorMessage = "Veuillez sélectionner un élève.";
            return;
        }

        var durchlaufId = ausgewaehlterDurchlauf.DurchlaufID;
        await ActionGuard.RunAsync(async () =>
        {
            var (ok, err, _) = await FinanzService.ZahlungBuchenAsync(
                durchlaufId,
                zahlungForm.StudentId,
                zahlungForm.Betrag,
                zahlungForm.ZahlungsArt,
                zahlungForm.Wirkungsdatum,
                currentUserId,
                zahlungForm.BeschreibungHtml,
                istSuperAdmin,
                autorisierungErzwingen: true);

            if (ok)
            {
                message = "Le paiement a été enregistré.";
                errorMessage = null;
                var aktualisiert = await FinanzService.GetDurchlaufUebersichtAsync(durchlaufId);
                ausgewaehlterDurchlauf = aktualisiert;
                if (aktiveSchulID is not null)
                {
                    durchlaeufe = await FinanzService.GetDurchlaeufeAsync(aktiveSchulID.Value);
                    await LadeUebersichtAsync();
                }
                zahlungForm = new() { Wirkungsdatum = DateTime.Today };
            }
            else
            {
                errorMessage = err;
            }
        });
    }

    private async Task DruckenAsync()
        => await JS.InvokeVoidAsync("SKOULOUAPPPrint.open");

    // -------------------- Formularmodelle --------------------

    private sealed class BuchungFormModel
    {
        public Guid KategorieID { get; set; }
        public decimal Betrag { get; set; }
        public DateTime Wirkungsdatum { get; set; } = DateTime.Today;
        public string? BeschreibungHtml { get; set; }
    }

    private sealed class DurchlaufFormModel
    {
        public Guid KlasseId { get; set; }
        public decimal NiveauPreis { get; set; }
        public DateTime StartDatum { get; set; } = DateTime.Today;
    }

    private bool durchlaufStartModalOpen;

    private Task DurchlaufStartModalOeffnenAsync()
    {
        if (durchlaufForm.KlasseId == Guid.Empty)
        {
            errorMessage = "Veuillez sélectionner une classe.";
            return Task.CompletedTask;
        }

        errorMessage = null;
        durchlaufStartModalOpen = true;
        return Task.CompletedTask;
    }

    private void DurchlaufStartModalSchliessen()
        => durchlaufStartModalOpen = false;

    private sealed class ZahlungFormModel
    {
        public Guid StudentId { get; set; }
        public decimal Betrag { get; set; }
        public ZahlungsArt ZahlungsArt { get; set; } = ZahlungsArt.Rate;
        public DateTime Wirkungsdatum { get; set; } = DateTime.Today;
        public string? BeschreibungHtml { get; set; }
    }
}
