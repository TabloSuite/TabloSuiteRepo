using ENote.Core.DataModels.Abo;
using ENote.Core.Services;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Admin(
    AdminService adminService,
    AdminKontenService adminKontenService,
    ProduktOwnerService produktOwnerService,
    Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider authStateProvider,
    ToastService toast,
    LadeZustand ladeZustand)
{
    private List<AdminService.SalonUebersicht> salons = [];
    private List<PlattformPreis> plattformPreise = [];
    private bool isLoading = true;
    private bool istSuperAdmin;
    private bool istErsterAdmin;
    private Guid aktuellerBenutzerId;

    // ProduktOwner-Verwaltung
    private List<ENote.Core.DataModels.Identity.AppUser> produktOwners = [];
    private List<ProduktOwnerService.Zuweisung> zuweisungen = [];
    private Guid? ausgewaehlterPoId;
    private string ausgewaehlterPoName = string.Empty;
    private bool poAnlegeDialogOffen;
    private string? poFehler;
    private string poVorname = string.Empty;
    private string poNachname = string.Empty;
    private string poPseudonym = string.Empty;
    private string poEmail = string.Empty;
    private string poPasswort = string.Empty;

    private bool provisionDialogOffen;
    private string? provisionFehler;
    private ProduktOwnerService.Zuweisung? provisionZuweisung;
    private decimal? provisionProzent;
    private decimal? provisionFestbetrag;
    private string ProvisionDialogTitel => "Commission – " + (provisionZuweisung?.SalonName ?? "");


    // Suche für Propriétaires plateforme
    private string poSuche = string.Empty;

    // Suche/Filter für die verwalteten Läden des ausgewählten PO
    private string verwalteteLaedenSuche = string.Empty;

    // Suche/Filter für die verfügbaren Läden (Checkbox-Liste)
    private string verfuegbareLaedenSuche = string.Empty;
    private string verfuegbareLaedenFilter = "alle"; // alle | gérés | non gérés

    private IEnumerable<ProduktOwnerService.Zuweisung> GefilterteVerwalteteLaeden()
    {
        if (zuweisungen is null) return Enumerable.Empty<ProduktOwnerService.Zuweisung>();
        if (string.IsNullOrWhiteSpace(verwalteteLaedenSuche)) return zuweisungen;
        var s = verwalteteLaedenSuche.Trim();
        return zuweisungen.Where(z => z.SalonName.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<AdminService.SalonUebersicht> GefilterteVerfuegbareLaeden()
    {
        // Suche läuft in der Datenbank (poSalons), die Chips filtern die geladene Seite.
        IEnumerable<AdminService.SalonUebersicht> q = poSalons;

        q = verfuegbareLaedenFilter switch
        {
            "gérés" => q.Where(x => zuweisungen.Any(z => z.SalonId == x.SalonId)),
            "non gérés" => q.Where(x => !zuweisungen.Any(z => z.SalonId == x.SalonId)),
            _ => q
        };

        return q;
    }

    private IEnumerable<ENote.Core.DataModels.Identity.AppUser> GefilterteProduktOwners()
    {
        if (string.IsNullOrWhiteSpace(poSuche))
        {
            return produktOwners;
        }

        var s = poSuche.Trim();
        return produktOwners.Where(p =>
            (p.FullName?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
            || (p.Email?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private bool probeDialogOffen;
    private Guid probeSalonId;
    private string probeSalonName = string.Empty;
    private DateTime? probeBisDatum;
    private string ProbeDialogTitel => "Essai gratuit – " + probeSalonName;

    // Seitenweise geladen (nie alle Läden), Kennzahlen per Datenbank über alle Läden.
    private List<AdminService.SalonUebersicht> ausstehende = [];
    private AdminService.SalonKennzahlen kennzahlen = new(0, 0, 0, 0, 0);
    private bool salonsMehr;
    private List<AdminService.SalonUebersicht> poSalons = [];
    private bool poSalonsMehr;
    private CancellationTokenSource? suchVerzoegerung;

    private async Task VerzoegertAsync(Func<Task> aktion)
    {
        suchVerzoegerung?.Cancel();
        var cts = suchVerzoegerung = new CancellationTokenSource();
        try { await Task.Delay(350, cts.Token); }
        catch (TaskCanceledException) { return; }
        await aktion();
    }

    private Task SucheSalonsVerzoegertAsync() => VerzoegertAsync(LadeSalonsAsync);
    private Task SuchePoSalonsVerzoegertAsync() => VerzoegertAsync(LadePoSalonsAsync);

    private async Task LadeSalonsAsync()
        => (salons, salonsMehr) = await adminService.SucheSalonsAsync(aktuellerBenutzerId, istSuperAdmin, salonSuche, salonStatusFilter, salonLandFilter, 0);

    private async Task MehrSalonsAsync()
    {
        var (neu, mehr) = await adminService.SucheSalonsAsync(aktuellerBenutzerId, istSuperAdmin, salonSuche, salonStatusFilter, salonLandFilter, salons.Count);
        salons.AddRange(neu);
        salonsMehr = mehr;
    }

    private async Task LadePoSalonsAsync()
        => (poSalons, poSalonsMehr) = await adminService.SucheSalonsAsync(aktuellerBenutzerId, istSuperAdmin, verfuegbareLaedenSuche, null, null, 0);

    private async Task MehrPoSalonsAsync()
    {
        var (neu, mehr) = await adminService.SucheSalonsAsync(aktuellerBenutzerId, istSuperAdmin, verfuegbareLaedenSuche, null, null, poSalons.Count);
        poSalons.AddRange(neu);
        poSalonsMehr = mehr;
    }

    // Tabs
    private string aktiverTab = "etablissements";

    /// <summary>Direkter Einstieg in einen Reiter, z. B. aus einer Mail (« /admin?tab=formation »).</summary>
    [SupplyParameterFromQuery(Name = "tab")] public string? StartTab { get; set; }

    // Suche/Filter für Établissements
    private string salonSuche = string.Empty;
    private string salonStatusFilter = "alle"; // alle | validé | attente
    private string? salonLandFilter;


    // Suche, Status und Land laufen in der Datenbank (SucheSalonsAsync).
    private IEnumerable<AdminService.SalonUebersicht> GefilterteSalons() => salons;


    private void WechsleTab(string tab) => aktiverTab = tab;


    // Tarif-Dialog (zentral, nicht innerhalb der scrollbaren Salon-Liste)
    private bool tarifDialogOffen;
    private string? tarifFehler;
    private Guid tarifSalonId;
    private decimal? tarifPreis;
    private AdminService.LadenTarif? tarif;
    private string TarifDialogTitel => tarif is null ? "Tarif mensuel de l'établissement" : "Tarif mensuel · " + tarif.Name;

    private bool besitzerDialogOffen;
    private string? besitzerFehler;
    private string neuVorname = string.Empty;
    private string neuNachname = string.Empty;
    private string neuPseudonym = string.Empty;
    private string neuEmail = string.Empty;
    private string neuTelefon = string.Empty;
    private string neuPasswort = string.Empty;
    private string neuSalonName = string.Empty;
    private string? neuStandort;
    private string? neuLand;

    private static readonly IReadOnlyList<ENote.WebApp.Components.Shared.EnoteSelectOption<string>> landOptionen =
        ENote.Core.DataModels.Salons.Laender.Alle
            .Select(l => new ENote.WebApp.Components.Shared.EnoteSelectOption<string> { Value = l, Label = l })
            .ToList();

    private static readonly IReadOnlyList<ENote.WebApp.Components.Shared.EnoteSelectOption<string>> statusFilterOptionen =
    [
        new() { Value = "alle", Label = "Tous les statuts" },
        new() { Value = "validé", Label = "Validé" },
        new() { Value = "attente", Label = "En attente" }
    ];

    private static readonly IReadOnlyList<ENote.WebApp.Components.Shared.EnoteSelectOption<string>> landFilterOptionen =
        ENote.Core.DataModels.Salons.Laender.Alle
            .Select(l => new ENote.WebApp.Components.Shared.EnoteSelectOption<string> { Value = l, Label = l })
            .ToList();

    protected override async Task OnInitializedAsync()
    {
        using var _ = ladeZustand.Beginnen();
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        istSuperAdmin = authState.User.IsInRole(ENote.Core.DataModels.Identity.AppRollen.SuperAdmin);
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(userIdValue, out aktuellerBenutzerId);
        if (istSuperAdmin && StartTab is "formation" or "plateforme" or "tarifs" or "mobilemoney") aktiverTab = StartTab;
        // Nur der Hauptadministrator verwaltet andere Administratoren (Reiter „Administrateurs“).
        istErsterAdmin = istSuperAdmin && await adminKontenService.IstErsterAdminAsync(aktuellerBenutzerId);
        await LadeAsync();
    }

    private async Task LadeAsync()
    {
        isLoading = true;
        kennzahlen = await adminService.GetSalonKennzahlenAsync(aktuellerBenutzerId, istSuperAdmin);
        await LadeSalonsAsync();
        (ausstehende, _) = await adminService.SucheSalonsAsync(aktuellerBenutzerId, istSuperAdmin, null, "attente", null, 0);
        plattformPreise = await adminService.GetPlattformPreiseAsync();
        globalerMonatsPreis = await adminService.GetGlobalenMonatsPreisAsync();
        if (istSuperAdmin)
        {
            produktOwners = await adminService.GetProduktOwnersAsync();
        }
        isLoading = false;
    }

    private async Task SetzeFreigabeAsync(Guid salonId, bool freigegeben)
    {
        try
        {
            await adminService.SetzeFreigabeAsync(aktuellerBenutzerId, salonId, freigegeben);
            toast.ZeigeErfolg(freigegeben ? "Salon validé." : "Validation retirée.");
        }
        catch (UnauthorizedAccessException ex)
        {
            toast.ZeigeFehler(ex.Message);
        }
        await LadeAsync();
    }

    private async Task SetzeAktivAsync(Guid salonId, bool aktiv)
    {
        try
        {
            await adminService.SetzeAktivAsync(aktuellerBenutzerId, salonId, aktiv);
            toast.ZeigeInfo(aktiv ? "Salon réactivé." : "Salon désactivé.");
        }
        catch (UnauthorizedAccessException ex)
        {
            toast.ZeigeFehler(ex.Message);
        }
        await LadeAsync();
    }

    // Land eines Ladens (nur SuperAdmin): Ortszeit und Mobile Money.
    private bool landDialogOffen;
    private Guid landSalonId;
    private string landSalonName = string.Empty;
    private string? landBisher;
    private string landNeu = ENote.WebApp.Services.LaenderAuswahl.Standard;
    private string? landFehler;
    private IReadOnlyList<ENote.WebApp.Components.Shared.EnoteSelectOption<string>> landAuswahlOptionen = [];

    private async Task OeffneLandDialog(Guid salonId, string name, string? land)
    {
        if (landAuswahlOptionen.Count == 0)
            landAuswahlOptionen = (await adminService.GetLaenderAsync())
                .Select(l => new ENote.WebApp.Components.Shared.EnoteSelectOption<string> { Value = l, Label = l }).ToList();
        landSalonId = salonId;
        landSalonName = name;
        landBisher = land;
        landNeu = land ?? ENote.WebApp.Services.LaenderAuswahl.Standard;
        landFehler = null;
        landDialogOffen = true;
    }

    private async Task SpeichereLandAsync()
    {
        try
        {
            await adminService.SetzeLandAsync(aktuellerBenutzerId, landSalonId, landNeu);
            landDialogOffen = false;
            toast.ZeigeErfolg($"Pays de « {landSalonName} » : {landNeu}.");
            await LadeSalonsAsync();
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            landFehler = ex.Message;
        }
    }

    // Anfrage eines Responsables an den Admin (kritische Aktionen führt nur der Admin aus).
    private bool anfrageDialogOffen;
    private Guid anfrageSalonId;
    private string anfrageSalonName = string.Empty;
    private string? anfrageAnliegen;
    private string? anfrageGrund;
    private string? anfrageFehler;
    private const string AnfrageRefus = "Refuser l'inscription";
    private string AnfrageDialogTitel => "Demande à l'administrateur – " + anfrageSalonName;

    private static readonly IReadOnlyList<ENote.WebApp.Components.Shared.EnoteSelectOption<string>> anfrageOptionen =
        new[] { "Désactiver l'établissement", "Réactiver l'établissement", "Refuser l'inscription", "Raccourcir ou retirer l'essai", "Modifier le tarif", "Autre demande" }
            .Select(a => new ENote.WebApp.Components.Shared.EnoteSelectOption<string> { Value = a, Label = a })
            .ToList();

    private void OeffneAnfrageDialog(Guid salonId, string salonName, string? anliegen = null)
    {
        anfrageSalonId = salonId;
        anfrageSalonName = salonName;
        anfrageAnliegen = anliegen;
        anfrageGrund = null;
        anfrageFehler = null;
        anfrageDialogOffen = true;
    }

    private async Task SendeAnfrageAsync()
    {
        if (string.IsNullOrWhiteSpace(anfrageAnliegen)) { anfrageFehler = "Veuillez choisir l'objet de la demande."; return; }
        try
        {
            await adminService.MeldeAnfrageAsync(aktuellerBenutzerId, anfrageSalonId, anfrageAnliegen, anfrageGrund);
            anfrageDialogOffen = false;
            toast.ZeigeErfolg("Demande envoyée à l'administrateur.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            anfrageFehler = ex.Message;
        }
    }


    private void OeffneBesitzerDialog()
    {
        neuVorname = neuNachname = neuPseudonym = neuEmail = neuTelefon = neuSalonName = string.Empty;
        neuStandort = null;
        neuLand = "Togo";
        neuPasswort = ErzeugeInitialPasswort();
        besitzerFehler = null;
        besitzerDialogOffen = true;
    }

    /// <summary>Pflichtangaben « Ajouter un propriétaire » (wie die Prüfung unten): alles außer Ville/quartier.</summary>
    private bool BesitzerVollstaendig => new[] { neuVorname, neuNachname, neuPseudonym, neuEmail, neuPasswort, neuSalonName, neuTelefon }
        .All(w => !string.IsNullOrWhiteSpace(w));

    private bool PoVollstaendig => new[] { poVorname, poNachname, poPseudonym, poEmail, poPasswort }.All(w => !string.IsNullOrWhiteSpace(w));

    private async Task LegeBesitzerAnAsync()
    {
        if (string.IsNullOrWhiteSpace(neuVorname) || string.IsNullOrWhiteSpace(neuNachname)
            || string.IsNullOrWhiteSpace(neuPseudonym) || string.IsNullOrWhiteSpace(neuEmail)
            || string.IsNullOrWhiteSpace(neuSalonName) || string.IsNullOrWhiteSpace(neuPasswort) || string.IsNullOrWhiteSpace(neuTelefon))
        {
            besitzerFehler = "Veuillez remplir tous les champs obligatoires.";
            return;
        }

        // SuperAdmin-Anlage: Login = eindeutiges Pseudonym, Kontakt-E-Mail darf mehrfach sein.
        var (erfolg, fehler, _) = await adminService.LegeBesitzerMitSalonAnAsync(
            aktuellerBenutzerId, neuVorname, neuNachname, neuEmail, neuPasswort, neuSalonName,
            neuStandort, neuLand, benutzername: neuPseudonym, telefon: neuTelefon);
        if (!erfolg)
        {
            besitzerFehler = fehler;
            return;
        }

        besitzerDialogOffen = false;
        toast.ZeigeErfolg("Propriétaire créé. Communiquez-lui son mot de passe initial.");
        await LadeAsync();
    }

    private static string ErzeugeInitialPasswort()
    {
        const string zeichen = "abcdefghjkmnpqrstuvwxyz23456789";
        var zufall = new Random();
        return new string(Enumerable.Range(0, 10).Select(_ => zeichen[zufall.Next(zeichen.Length)]).ToArray());
    }

    private void OeffnePoAnlegeDialog()
    {
        poVorname = poNachname = poPseudonym = poEmail = string.Empty;
        poPasswort = ErzeugeInitialPasswort();
        poFehler = null;
        poAnlegeDialogOffen = true;
    }

    private async Task LegeProduktOwnerAnAsync()
    {
        if (string.IsNullOrWhiteSpace(poVorname) || string.IsNullOrWhiteSpace(poNachname)
            || string.IsNullOrWhiteSpace(poPseudonym) || string.IsNullOrWhiteSpace(poEmail)
            || string.IsNullOrWhiteSpace(poPasswort))
        {
            poFehler = "Veuillez remplir tous les champs.";
            return;
        }

        var (erfolg, fehler, _) = await adminService.LegeProduktOwnerAnAsync(poVorname, poNachname, poPseudonym, poEmail, poPasswort);
        if (!erfolg)
        {
            poFehler = fehler;
            return;
        }

        poAnlegeDialogOffen = false;
        toast.ZeigeErfolg("Propriétaire plateforme créé. Communiquez-lui son mot de passe initial.");
        await LadeAsync();
    }

    private async Task WaehleProduktOwnerAsync(Guid poId, string name)
    {
        ausgewaehlterPoId = poId;
        ausgewaehlterPoName = name;
        zuweisungen = await produktOwnerService.GetZuweisungenAsync(poId);
    }

    private async Task ToggleZuweisungAsync(Guid salonId, bool zugewiesen)
    {
        if (ausgewaehlterPoId is not Guid poId)
        {
            return;
        }

        try
        {
            if (zugewiesen)
            {
                // Ein Laden hat genau einen Responsable: war er bei einem anderen, wechselt er (Hinweis statt stillem Wechsel).
                var laden = poSalons.FirstOrDefault(s => s.SalonId == salonId);
                await produktOwnerService.WeiseSalonZuAsync(poId, salonId, kannLadenUmsatz: true, kannPlattformUmsatz: false);
                if (laden is { ProduktOwnerName: { Length: > 0 } bisher } && bisher != ausgewaehlterPoName)
                    toast.ZeigeInfo($"« {laden.Name} » était suivi par {bisher} : il est maintenant suivi par {ausgewaehlterPoName}.");
            }
            else
            {
                var z = zuweisungen.FirstOrDefault(x => x.SalonId == salonId);
                if (z is not null)
                {
                    await produktOwnerService.EntferneZuweisungAsync(z.ZuweisungId);
                }
            }
            // Spalte « PO » der geladenen Liste nachziehen (ohne neu zu laden – « Afficher plus » bleibt erhalten).
            var i = poSalons.FindIndex(s => s.SalonId == salonId);
            if (i >= 0) poSalons[i] = poSalons[i] with { ProduktOwnerName = zugewiesen ? ausgewaehlterPoName : null };
        }
        catch (Exception ex) when (ex is Microsoft.EntityFrameworkCore.DbUpdateException or InvalidOperationException)
        {
            toast.ZeigeFehler("L'affectation n'a pas pu être enregistrée. Rechargez la page et réessayez.");
        }

        zuweisungen = await produktOwnerService.GetZuweisungenAsync(poId);
    }

    private async Task SetzeUmsatzRechtAsync(ProduktOwnerService.Zuweisung z)
    {
        if (ausgewaehlterPoId is not Guid poId)
        {
            return;
        }

        await produktOwnerService.WeiseSalonZuAsync(
            poId, z.SalonId,
            !z.KannLadenUmsatzSehen,
            z.KannPlattformUmsatzSehen); // Plattform-Umsatz bleibt unverändert (nicht mehr in UI)
        zuweisungen = await produktOwnerService.GetZuweisungenAsync(poId);
    }

    private void OeffneProvisionDialog(ProduktOwnerService.Zuweisung z)
    {
        provisionZuweisung = z;
        provisionProzent = z.ProzentProvision;
        provisionFestbetrag = z.FestbetragProvision;
        provisionFehler = null;
        provisionDialogOffen = true;
    }

    private async Task OeffneProbeDialogAsync(Guid salonId, string salonName)
    {
        probeSalonId = salonId;
        probeSalonName = salonName;
        probeBisDatum = await adminService.GetProbezeitAsync(salonId);
        probeBisher = probeBisDatum;
        probeFehler = null;
        probeDialogOffen = true;
    }

    private string? probeFehler;
    private DateTime? probeBisher;

    /// <summary>Responsable darf nur eine laufende Probezeit verlängern, die noch vor seiner Grenze (30 Tage ab heute) endet.</summary>
    private bool PoDarfVerlaengern => !istSuperAdmin && probeBisher is DateTime bisher && bisher < AdminService.PoProbeGrenze(DateTime.UtcNow);
    private bool ZeigeProbeFormular => istSuperAdmin || PoDarfVerlaengern;

    private async Task SpeichereProbeAsync()
    {
        try
        {
            await adminService.SetzeProbezeitAsync(aktuellerBenutzerId, probeSalonId, probeBisDatum);
        }
        catch (UnauthorizedAccessException ex)
        {
            probeFehler = ex.Message;
            return;
        }
        probeDialogOffen = false;
        toast.ZeigeErfolg("Essai mis à jour.");
        await LadeAsync();
    }

    private void SetzeProbeStandard()
        => probeBisDatum = AuthService.BerechneProbeEnde(DateTime.UtcNow);

    /// <summary>Responsable: Probezeit bis zur erlaubten Grenze verlängern (höchstens 30 Tage ab heute).</summary>
    private void SetzeProbeMaximal()
        => probeBisDatum = AdminService.PoProbeGrenze(DateTime.UtcNow).Date;

    private void EntferneProbe() => probeBisDatum = null;

    // Tarif-Dialog: öffnen/schließen/speichern (zentral am Seitenende gerendert)
    private async Task OeffneTarifDialogAsync(Guid salonId)
    {
        tarifDialogOffen = true;
        tarifFehler = null;
        tarif = null;
        tarifSalonId = salonId;
        try
        {
            tarif = await adminService.GetLadenTarifAsync(aktuellerBenutzerId, salonId);
            tarifPreis = tarif.Individuell ?? tarif.Global;
        }
        catch (Exception)
        {
            tarifFehler = "Le chargement du tarif a échoué. Fermez puis réessayez.";
        }
    }

    private async Task SpeichereTarifAsync(bool standard)
    {
        try
        {
            if (!standard && tarifPreis is not > 0) { tarifFehler = "Veuillez saisir un montant positif."; return; }
            if (tarif is null) return;
            if (!await adminService.SetzeIndividuellenPreisAsync(aktuellerBenutzerId, tarifSalonId, standard ? null : tarifPreis))
            {
                tarifFehler = "Établissement introuvable.";
                return;
            }
            tarifDialogOffen = false;
            tarifFehler = null;
            toast.ZeigeErfolg(standard ? "Tarif général appliqué." : "Tarif personnalisé enregistré.");
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // z. B. Preis unter dem Standard – die Meldung steht nach „ (Parameter …)“ ohne den Zusatz.
            tarifFehler = ex.Message.Split(" (Parameter")[0];
        }
        catch (Exception)
        {
            tarifFehler = "L'enregistrement du tarif a échoué.";
        }
    }

    private async Task SpeichereProvisionAsync()
    {
        if (provisionZuweisung is null)
        {
            return;
        }

        var erfolg = await produktOwnerService.SetzeProvisionAsync(provisionZuweisung.ZuweisungId, provisionProzent, provisionFestbetrag);
        if (!erfolg)
        {
            provisionFehler = "Valeur invalide (pourcentage 0–100, montant ≥ 0).";
            return;
        }

        provisionDialogOffen = false;
        toast.ZeigeErfolg("Commission enregistrée.");
        if (ausgewaehlterPoId is Guid poId)
        {
            zuweisungen = await produktOwnerService.GetZuweisungenAsync(poId);
        }
    }

    private static string IntervallText(AboIntervall intervall) => intervall switch
    {
        AboIntervall.Monatlich => "Mensuel",
        AboIntervall.DreiMonate => "3 mois",
        AboIntervall.SechsMonate => "6 mois",
        AboIntervall.Jaehrlich => "Annuel",
        _ => intervall.ToString()
    };


    /// <summary>Globaler Monatspreis (Basis aller Formeln).</summary>
    private decimal globalerMonatsPreis;
    private decimal GlobalerMonatsPreis => globalerMonatsPreis;

}
