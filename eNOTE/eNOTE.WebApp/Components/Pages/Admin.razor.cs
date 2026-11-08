using ENote.Core.DataModels.Abo;
using ENote.Core.Services;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Admin(
    AdminService adminService,
    ProduktOwnerService produktOwnerService,
    Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider authStateProvider,
    ToastService toast,
    LadeZustand ladeZustand)
{
    private List<AdminService.SalonUebersicht> salons = [];
    private List<PlattformPreis> plattformPreise = [];
    private bool isLoading = true;
    private bool istSuperAdmin;
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

    private DateTime? auszahlungMonat = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    private List<ProduktOwnerAuszahlung> auszahlungen = [];

    // Suche für Propriétaires plateforme
    private string poSuche = string.Empty;

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

    private List<AdminService.SalonUebersicht> ausstehende =>
        salons.Where(s => !s.IstFreigegeben && s.IstAktiv).ToList();

    // Tabs
    private string aktiverTab = "etablissements";

    // Suche/Filter für Établissements
    private string salonSuche = string.Empty;
    private string salonStatusFilter = "alle"; // alle | validé | attente
    private string? salonLandFilter;
    private string? salonBesitzerFilter;

    private IReadOnlyList<ENote.WebApp.Components.Shared.EnoteSelectOption<string>> besitzerFilterOptionen =>
        salons.Where(s => !string.IsNullOrWhiteSpace(s.BesitzerName))
            .Select(s => s.BesitzerName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .Select(n => new ENote.WebApp.Components.Shared.EnoteSelectOption<string> { Value = n, Label = n })
            .ToList();

    private IEnumerable<AdminService.SalonUebersicht> GefilterteSalons()
    {
        IEnumerable<AdminService.SalonUebersicht> q = salons;

        if (!string.IsNullOrWhiteSpace(salonSuche))
        {
            var s = salonSuche.Trim();
            q = q.Where(x =>
                x.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
                || (x.Standort?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        q = salonStatusFilter switch
        {
            "validé" => q.Where(x => x.IstFreigegeben),
            "attente" => q.Where(x => !x.IstFreigegeben && x.IstAktiv),
            _ => q
        };

        if (!string.IsNullOrWhiteSpace(salonLandFilter))
        {
            q = q.Where(x => string.Equals(x.Land, salonLandFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(salonBesitzerFilter))
        {
            q = q.Where(x => string.Equals(x.BesitzerName, salonBesitzerFilter, StringComparison.OrdinalIgnoreCase));
        }

        return q;
    }

    private void WechsleTab(string tab) => aktiverTab = tab;

    private bool preisDialogOffen;
    private string? preisFehler;
    private AboIntervall aktuellesIntervall;
    private decimal neuerPreis;

    private bool besitzerDialogOffen;
    private string? besitzerFehler;
    private string neuVorname = string.Empty;
    private string neuNachname = string.Empty;
    private string neuPseudonym = string.Empty;
    private string neuEmail = string.Empty;
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
        await LadeAsync();
    }

    private async Task LadeAsync()
    {
        isLoading = true;
        salons = await adminService.GetSalonUebersichtFuerAsync(aktuellerBenutzerId, istSuperAdmin);
        plattformPreise = await adminService.GetPlattformPreiseAsync();
        if (istSuperAdmin)
        {
            produktOwners = await adminService.GetProduktOwnersAsync();
        }
        isLoading = false;
    }

    private async Task SetzeFreigabeAsync(Guid salonId, bool freigegeben)
    {
        await adminService.SetzeFreigabeAsync(salonId, freigegeben);
        toast.ZeigeErfolg(freigegeben ? "Salon validé." : "Validation retirée.");
        await LadeAsync();
    }

    private async Task SetzeAktivAsync(Guid salonId, bool aktiv)
    {
        await adminService.SetzeAktivAsync(salonId, aktiv);
        toast.ZeigeInfo(aktiv ? "Salon réactivé." : "Salon désactivé.");
        await LadeAsync();
    }

    private void OeffnePreisDialog(PlattformPreis preis)
    {
        aktuellesIntervall = preis.Intervall;
        neuerPreis = preis.Preis;
        preisFehler = null;
        preisDialogOffen = true;
    }

    private void OeffneBesitzerDialog()
    {
        neuVorname = neuNachname = neuPseudonym = neuEmail = neuSalonName = string.Empty;
        neuStandort = null;
        neuLand = "Togo";
        neuPasswort = ErzeugeInitialPasswort();
        besitzerFehler = null;
        besitzerDialogOffen = true;
    }

    private async Task LegeBesitzerAnAsync()
    {
        if (string.IsNullOrWhiteSpace(neuVorname) || string.IsNullOrWhiteSpace(neuNachname)
            || string.IsNullOrWhiteSpace(neuPseudonym) || string.IsNullOrWhiteSpace(neuEmail)
            || string.IsNullOrWhiteSpace(neuSalonName) || string.IsNullOrWhiteSpace(neuPasswort))
        {
            besitzerFehler = "Veuillez remplir tous les champs obligatoires.";
            return;
        }

        // SuperAdmin-Anlage: Login = eindeutiges Pseudonym, Kontakt-E-Mail darf mehrfach sein.
        var (erfolg, fehler, _) = await adminService.LegeBesitzerMitSalonAnAsync(
            aktuellerBenutzerId, neuVorname, neuNachname, neuEmail, neuPasswort, neuSalonName,
            neuStandort, neuLand, benutzername: neuPseudonym);
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

        if (zugewiesen)
        {
            await produktOwnerService.WeiseSalonZuAsync(poId, salonId, kannLadenUmsatz: true, kannPlattformUmsatz: false);
        }
        else
        {
            var z = zuweisungen.FirstOrDefault(x => x.SalonId == salonId);
            if (z is not null)
            {
                await produktOwnerService.EntferneZuweisungAsync(z.ZuweisungId);
            }
        }

        zuweisungen = await produktOwnerService.GetZuweisungenAsync(poId);
    }

    private async Task SetzeUmsatzRechtAsync(ProduktOwnerService.Zuweisung z, bool plattform)
    {
        if (ausgewaehlterPoId is not Guid poId)
        {
            return;
        }

        await produktOwnerService.WeiseSalonZuAsync(
            poId, z.SalonId,
            plattform ? z.KannLadenUmsatzSehen : !z.KannLadenUmsatzSehen,
            plattform ? !z.KannPlattformUmsatzSehen : z.KannPlattformUmsatzSehen);
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

    private async Task BerechneAuszahlungenAsync()
    {
        var monat = auszahlungMonat ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        foreach (var po in produktOwners)
        {
            await produktOwnerService.BerechneAuszahlungAsync(po.Id, monat.Year, monat.Month);
        }

        auszahlungen = await produktOwnerService.GetAuszahlungenAsync(monat.Year, monat.Month);
        toast.ZeigeErfolg("Commissions calculées.");
    }

    private async Task BestaetigeAuszahlungAsync(Guid auszahlungId)
    {
        await produktOwnerService.BestaetigeAuszahlungAsync(auszahlungId);
        toast.ZeigeErfolg("Versement confirmé.");
        var monat = auszahlungMonat ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        auszahlungen = await produktOwnerService.GetAuszahlungenAsync(monat.Year, monat.Month);
    }

    private async Task OeffneProbeDialogAsync(Guid salonId, string salonName)
    {
        probeSalonId = salonId;
        probeSalonName = salonName;
        probeBisDatum = await adminService.GetProbezeitAsync(salonId);
        probeDialogOffen = true;
    }

    private async Task SpeichereProbeAsync()
    {
        await adminService.SetzeProbezeitAsync(probeSalonId, probeBisDatum);
        probeDialogOffen = false;
        toast.ZeigeErfolg("Essai mis à jour.");
        await LadeAsync();
    }

    private void SetzeProbeStandard()
        => probeBisDatum = AuthService.BerechneProbeEnde(DateTime.UtcNow);

    private void EntferneProbe() => probeBisDatum = null;

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

    private async Task SetzePreisAsync()
    {
        if (neuerPreis < 0)
        {
            preisFehler = "Le prix ne peut pas être négatif.";
            return;
        }

        await adminService.SetzePlattformPreisAsync(aktuellesIntervall, neuerPreis);
        preisDialogOffen = false;
        toast.ZeigeErfolg("Prix enregistré.");
        await LadeAsync();
    }

    private static string IntervallText(AboIntervall intervall) => intervall switch
    {
        AboIntervall.Monatlich => "Mensuel",
        AboIntervall.DreiMonate => "3 mois",
        AboIntervall.Jaehrlich => "Annuel",
        _ => intervall.ToString()
    };

    private string PreisDialogTitel => "Prix " + IntervallText(aktuellesIntervall);
}
