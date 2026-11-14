using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Skoulou.Core.Fehler;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Components.Shared;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Components.Pages.Auth;

/// <summary>
/// Registrierung « Direction » oder « Élève »: Prüfung in Blazor (Meldung am Feld), dann Nutzungsbedingungen,
/// dann klassisches Absenden an /auth/inscription/envoyer (Antiforgery, Rate-Limit, Honeypot).
/// </summary>
public partial class Inscription
{
    [Inject] private RegistrierungService Registrierung { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private TimeProvider Uhr { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "type")] public string? Typ { get; set; }
    [SupplyParameterFromQuery(Name = "envoye")] public string? Envoye { get; set; }
    [SupplyParameterFromQuery(Name = "identifiant")] public string? Identifiant { get; set; }
    [SupplyParameterFromQuery(Name = "erreur")] public string? Erreur { get; set; }

    private readonly string formId = Feld.NeueId("inscription");
    private RegistrierungsArt art = RegistrierungsArt.Direction;
    private RegistrierungsArt? gesendet;
    private string? schulName, ort, land = "Togo", vorname, nachname, email, telefon, identifiant, passwort, bestaetigung, elternTelefon, elternEmail;
    private DateOnly? geburtsdatum;
    private SchulTreffer? schule;
    private bool agb, agbOffen;
    private string? meldung, fehler, fehlerFeld;

    private DateOnly heute => new SchulZeit(Uhr).Heute;

    /// <summary>Fehlermeldung für ein Feld (Name wie im Service).</summary>
    private string? F(string feld) => fehlerFeld == feld ? fehler : null;

    protected override void OnParametersSet()
    {
        if (Envoye is "direction" or "eleve") gesendet = Envoye == "eleve" ? RegistrierungsArt.Eleve : RegistrierungsArt.Direction;
        art = Typ is "eleve" or "student" or "schueler" or "élève" ? RegistrierungsArt.Eleve : RegistrierungsArt.Direction;
        meldung = Erreur switch
        {
            null => null,
            "limite" => "Trop de tentatives en peu de temps. Patientez quelques minutes, puis réessayez.",
            "bremse" => RegistrierungsBremse.Meldung,
            "session" => "La page a expiré. Veuillez vérifier vos informations et réessayer.",
            "agb" => RegistrierungService.MeldungAgb,
            _ => "L'inscription n'a pas pu être enregistrée. Vérifiez vos informations et réessayez."
        };
    }

    private void Waehlen(RegistrierungsArt neu)
    {
        art = neu;
        fehler = fehlerFeld = null;
    }

    private RegistrierungsEingabe Eingabe(bool agbAkzeptiert) => new(art, vorname, nachname, identifiant, email, telefon, passwort, bestaetigung, agbAkzeptiert,
        schulName, ort, land, schule?.Id, geburtsdatum, elternTelefon, elternEmail);

    private async Task VorschlagAsync()
    {
        if (string.IsNullOrWhiteSpace(vorname) || string.IsNullOrWhiteSpace(nachname))
        {
            (fehler, fehlerFeld) = ("Indiquez d'abord votre prénom et votre nom.", "identifiant");
            return;
        }
        identifiant = await Registrierung.IdentifiantVorschlagAsync(vorname, nachname);
        fehler = fehlerFeld = null;
    }

    /// <summary>« Continuer »: alles prüfen (ohne Anlage), dann die Nutzungsbedingungen zeigen.</summary>
    private async Task WeiterAsync()
    {
        if (agbOffen) return;
        try
        {
            await Registrierung.PruefeAsync(Eingabe(false));
            fehler = fehlerFeld = meldung = null;
            agbOffen = true;
        }
        catch (FachFehler f)
        {
            (fehler, fehlerFeld) = (f.Message, f.Feld);
            meldung = f.Feld is null ? f.Message : null;
        }
    }

    /// <summary>Bedingungen akzeptiert: Feld « agb » setzen und das Formular klassisch absenden.</summary>
    private async Task AkzeptiertAsync()
    {
        agb = true;
        StateHasChanged();
        await Task.Yield(); // erst rendern (verstecktes Feld), dann absenden
        await JS.InvokeVoidAsync("skFormularSenden", formId);
    }
}
