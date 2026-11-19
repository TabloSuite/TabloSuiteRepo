using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Skoulou.Core.Fehler;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Components.Shared;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Components.Pages.Auth;

/// <summary>
/// Registrierung « Direction » oder « Élève »: Prüfung in Blazor (Meldung am Feld), Zustimmung zu den Nutzungsbedingungen
/// per Kästchen im Formular (wie eNOTE), dann klassisches Absenden an /auth/inscription/envoyer (Antiforgery, Rate-Limit, Honeypot).
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

    /// <summary>
    /// « Créer mon compte » gesperrt, solange eine Pflichtangabe (Stern) fehlt. Nur interaktiv: vorgerendert (noch ohne
    /// Verbindung) bleibt der Knopf frei, das klassische Absenden prüft dann der Server. Die Zustimmung zu den Bedingungen
    /// sperrt nicht – dafür gibt es die Meldung am Kästchen.
    /// </summary>
    private bool Gesperrt => RendererInfo.IsInteractive && !(Feld.Ausgefuellt(vorname, nachname, passwort, bestaetigung)
        && (art == RegistrierungsArt.Direction ? Feld.Ausgefuellt(schulName, ort, email, telefon) : schule is not null));

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

    /// <summary>
    /// « Créer mon compte »: alles prüfen (ohne Anlage), die Zustimmung zu den Bedingungen verlangen (Kästchen im
    /// Formular wie bei eNOTE) und das Formular klassisch absenden. Der Ladekreis des Knopfs erscheint sofort im Browser
    /// (skoulou.js) und bleibt bis zur neuen Seite; bei einem Fehler gibt « skFormularFrei » den Knopf wieder frei.
    /// </summary>
    private async Task WeiterAsync()
    {
        if (agbOffen)
        {
            await JS.InvokeVoidAsync("skFormularFrei", formId);
            return;
        }
        try
        {
            await Registrierung.PruefeAsync(Eingabe(agb));
            if (!agb) throw new FachFehler(RegistrierungService.MeldungAgb, "agb");
            fehler = fehlerFeld = meldung = null;
            await JS.InvokeVoidAsync("skFormularSenden", formId);
        }
        catch (FachFehler f)
        {
            (fehler, fehlerFeld) = (f.Message, f.Feld);
            meldung = f.Feld is null ? f.Message : null;
            await JS.InvokeVoidAsync("skFormularFrei", formId);
        }
    }

    private void AgbGeaendert(ChangeEventArgs e)
    {
        agb = e.Value is true;
        if (agb && fehlerFeld == "agb") fehler = fehlerFeld = null;
    }

    /// <summary>« J'accepte » im Fenster der Bedingungen: Kästchen setzen und Fenster schließen.</summary>
    private void Akzeptiert()
    {
        agb = true;
        agbOffen = false;
        if (fehlerFeld == "agb") fehler = fehlerFeld = null;
    }
}
