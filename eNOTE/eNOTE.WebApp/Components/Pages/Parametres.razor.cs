using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Parametres(
    SalonService salonService,
    MobileMoneyService mobileMoneyService,
    KatalogService katalogService,
    TeamService teamService,
    KundenBesuchService besuchService,
    ToastService toast,
    AktiverSalonState aktiverSalon,
    AuthenticationStateProvider authStateProvider,
    LadeZustand ladeZustand) : IDisposable
{
    private sealed class MomoEintrag(Core.DataModels.Kasse.MobileMoneyAnbieter anbieter, bool akzeptiert)
    {
        public Core.DataModels.Kasse.MobileMoneyAnbieter Anbieter { get; } = anbieter;
        public bool Akzeptiert { get; set; } = akzeptiert;
    }

    private bool isLoading = true;
    private string? land;
    private string landWahl = LaenderAuswahl.Standard;
    private IReadOnlyList<Shared.EnoteSelectOption<string>> landOptionen = [];
    private List<MomoEintrag> momoAuswahl = [];
    private Guid salonId;
    private Guid benutzerId;
    private string salonName = string.Empty;
    private string? slogan;
    private string? telefon;
    private string? adresse;
    private string? bonQrLink;
    private string? logoVorschau;
    private string? logoFehler;

    // Salles et postes (Schnellauswahl « Nouvelle arrivée »)
    private List<KundenBesuchService.Raum> raeume = [];
    private string neuerRaum = string.Empty;
    private string? raumFehler;

    private string neueBesitzerPin = string.Empty;
    private string? pinFehler;
    private bool hatBesitzerPin;
    private bool pinSichtbar;

    // Weiteren Laden hinzufügen
    private bool darfWeiterenLaden;
    private bool neuerLadenDialogOffen;
    private string neuerLadenName = string.Empty;
    private string? neuerLadenStandort;
    private string? neuerLadenFehler;
    // Neues Besitzer-Konto für den zweiten Laden (Regel: ein Konto pro Laden, E-Mail wiederverwendbar)
    private string neuerLadenVorname = string.Empty;
    private string neuerLadenNachname = string.Empty;
    private string neuerLadenPseudonym = string.Empty;
    private string neuerLadenEmail = string.Empty;
    private string neuerLadenPasswort = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        using var _ = ladeZustand.Beginnen();
        aktiverSalon.OnChange += HandleSalonWechsel;

        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out benutzerId))
        {
            isLoading = false;
            return;
        }

        if (!aktiverSalon.IstGeladen)
        {
            await aktiverSalon.InitialisiereAsync(benutzerId);
        }

        var aktive = aktiverSalon.Aktiv;
        if (aktive is null)
        {
            isLoading = false;
            return;
        }

        salonId = aktive.SalonId;
        await LadeSalonAsync();
        isLoading = false;
    }

    private void HandleSalonWechsel()
    {
        var aktive = aktiverSalon.Aktiv;
        if (aktive is null || aktive.SalonId == salonId)
        {
            return;
        }

        salonId = aktive.SalonId;
        _ = InvokeAsync(async () => { await LadeSalonAsync(); StateHasChanged(); });
    }

    public void Dispose() => aktiverSalon.OnChange -= HandleSalonWechsel;

    private async Task LadeSalonAsync()
    {
        var salon = await salonService.GetSalonAsync(salonId);
        if (salon is not null)
        {
            salonName = salon.Name;
            slogan = salon.Slogan;
            telefon = salon.TelefonNummer;
            adresse = salon.Adresse;
            bonQrLink = salon.BonQrLink;
            logoVorschau = salonService.GetLogoUrl(salon);
            land = salon.Land;
            landWahl = salon.Land ?? LaenderAuswahl.Standard;
        }

        if (landOptionen.Count == 0)
            landOptionen = (await salonService.GetLaenderAsync())
                .Select(l => new Shared.EnoteSelectOption<string> { Value = l, Label = l }).ToList();

        momoAuswahl = (await mobileMoneyService.GetAuswahlFuerLadenAsync(salonId))
            .Select(a => new MomoEintrag(a.Anbieter, a.Akzeptiert)).ToList();

        await LadeRaeumeAsync();
        hatBesitzerPin = await salonService.HatBesitzerPinAsync(salonId, benutzerId);
        darfWeiterenLaden = await salonService.DarfWeiterenLadenAnlegenAsync(benutzerId);
    }

    private async Task LadeRaeumeAsync()
    {
        try { raeume = await besuchService.GetRaeumeAsync(benutzerId, salonId); }
        catch (UnauthorizedAccessException) { raeume = []; }
    }

    private async Task RaumAktionAsync(Func<Task> aktion)
    {
        raumFehler = null;
        try { await aktion(); }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException) { raumFehler = ex.Message; }
        await LadeRaeumeAsync();
    }

    private Task RaumHinzufuegenAsync() => RaumAktionAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(neuerRaum)) return;
        var r = await besuchService.RaumAnlegenAsync(benutzerId, salonId, neuerRaum);
        neuerRaum = string.Empty;
        toast.ZeigeErfolg($"« {r.Name} » ajouté.");
    });

    private async Task RaumTasteAsync(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await RaumHinzufuegenAsync();
    }

    private Task RaumUmbenennenAsync(KundenBesuchService.Raum r, string? name) => RaumAktionAsync(async () =>
    {
        await besuchService.RaumUmbenennenAsync(benutzerId, r.Id, name ?? string.Empty);
    });

    private Task RaumVerschiebenAsync(KundenBesuchService.Raum r, int richtung)
        => RaumAktionAsync(() => besuchService.RaumVerschiebenAsync(benutzerId, r.Id, richtung));

    private Task RaumLoeschenAsync(KundenBesuchService.Raum r) => RaumAktionAsync(async () =>
    {
        await besuchService.RaumLoeschenAsync(benutzerId, r.Id);
        toast.ZeigeInfo($"« {r.Name} » supprimé.");
    });

    private void OeffneNeuerLadenDialog()
    {
        neuerLadenName = string.Empty;
        neuerLadenStandort = null;
        neuerLadenFehler = null;
        neuerLadenVorname = string.Empty;
        neuerLadenNachname = string.Empty;
        neuerLadenPseudonym = string.Empty;
        neuerLadenEmail = string.Empty;
        neuerLadenPasswort = string.Empty;
        neuerLadenDialogOffen = true;
    }

    private async Task FuegeNeuenLadenHinzuAsync()
    {
        var (erfolg, fehler, salon, _) = await salonService.FuegeSalonHinzuAsync(
            benutzerId, neuerLadenName, neuerLadenStandort, null,
            neuerLadenVorname, neuerLadenNachname, neuerLadenPseudonym, neuerLadenEmail, neuerLadenPasswort);
        if (!erfolg)
        {
            neuerLadenFehler = fehler;
            return;
        }

        neuerLadenDialogOffen = false;
        await aktiverSalon.AktualisiereLaedenAsync(benutzerId);
        toast.ZeigeErfolg($"Établissement « {salon!.Name} » créé avec un nouveau compte. Il sera activé après validation par l'administrateur.");
    }

    private byte[]? ausgewaehltesLogoBytes;
    private string ausgewaehltesLogoContentType = "image/jpeg";
    private string ausgewaehltesLogoName = "logo.jpg";

    private void HandleLogoBereit((byte[] Bytes, string ContentType, string DateiName) bild)
    {
        logoFehler = null;
        ausgewaehltesLogoBytes = bild.Bytes;
        ausgewaehltesLogoContentType = bild.ContentType;
        ausgewaehltesLogoName = bild.DateiName;
        logoVorschau = $"data:{bild.ContentType};base64,{Convert.ToBase64String(bild.Bytes)}";
    }

    private void HandleLogoEntfernt()
    {
        logoVorschau = null;
        ausgewaehltesLogoBytes = null;
    }

    private async Task SpeichernAsync()
    {
        await salonService.SetzeKontaktdatenAsync(salonId, slogan, telefon, adresse, bonQrLink);

        if (ausgewaehltesLogoBytes is not null)
        {
            using var ms = new MemoryStream(ausgewaehltesLogoBytes);
            var (erfolg, fehler, url) = await salonService.SetzeLogoAsync(
                salonId, ausgewaehltesLogoName, ausgewaehltesLogoContentType, ms, benutzerId);
            if (!erfolg)
            {
                toast.ZeigeFehler(fehler ?? "L'enregistrement du logo a échoué.");
                return;
            }

            logoVorschau = url;
        }

        toast.ZeigeErfolg("Paramètres enregistrés.");
    }

    /// <summary>Land ändern: Ortszeit und Mobile-Money-Anbieter folgen sofort (Kasse, Bon, Finances).</summary>
    private async Task SpeichereLandAsync()
    {
        try
        {
            await salonService.SetzeLandAsync(benutzerId, salonId, landWahl);
            // Ortszeit der Navigation/Kasse in diesem Browser nachziehen.
            await aktiverSalon.AktualisiereLaedenAsync(benutzerId);
            await LadeSalonAsync();
            toast.ZeigeErfolg($"Pays enregistré : {land}.");
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            toast.ZeigeFehler(ex.Message);
        }
    }

    private async Task SpeichereMomoAsync()
    {
        var (erfolg, fehler) = await mobileMoneyService.SetzeAuswahlAsync(
            benutzerId, salonId, momoAuswahl.Where(m => m.Akzeptiert).Select(m => m.Anbieter.Id).ToList());
        if (erfolg)
            toast.ZeigeErfolg(momoAuswahl.Any(m => m.Akzeptiert) ? "Opérateurs Mobile Money enregistrés." : "Mobile Money désactivé pour la caisse.");
        else
            toast.ZeigeFehler(fehler ?? "L'enregistrement a échoué.");
    }

    private static string Kuerzel(string name)
    {
        var teile = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return teile.Length >= 2 ? string.Concat(teile[0][0], teile[1][0]).ToUpperInvariant() : name[..Math.Min(2, name.Length)].ToUpperInvariant();
    }

    private async Task SpeichereBesitzerPinAsync()
    {
        pinFehler = null;

        if (string.IsNullOrWhiteSpace(neueBesitzerPin))
        {
            pinFehler = "Veuillez saisir un code PIN.";
            return;
        }

        var zuordnung = await katalogService.GetUserSalonAsync(benutzerId);
        if (zuordnung is null)
        {
            pinFehler = "Membre introuvable.";
            return;
        }

        var (erfolg, fehler) = await teamService.SetzeBesitzerPinAsync(zuordnung.Id, neueBesitzerPin);
        if (!erfolg)
        {
            pinFehler = fehler;
            return;
        }

        neueBesitzerPin = string.Empty;
        hatBesitzerPin = true;
        toast.ZeigeErfolg("Code PIN propriétaire enregistré.");
    }
}
