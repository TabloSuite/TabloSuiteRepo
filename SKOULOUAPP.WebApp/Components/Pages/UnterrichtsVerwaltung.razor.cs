using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using SkoulouApp.WebApp.Components.Shared;
using SkoulouApp.WebApp.Services;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class UnterrichtsVerwaltung
{
    [Inject] private UnterrichtsVerwaltungService Verwaltung { get; set; } = default!;
    [Inject] private AuthenticationStateProvider Auth { get; set; } = default!;
    [Inject] private ILogger<UnterrichtsVerwaltung> Logger { get; set; } = default!;
    private List<AuswahlEintrag> schulen = [];
    private UnterrichtsUebersicht? uebersicht;
    private SchulDokumentStammdaten dokumentDaten = new();
    private List<SchuelerStammdatenZeile> schuelerDaten = [];
    private Guid schuelerID;
    private string? matrikelnummer, geburtsort;
    private Task MatriculesAsync() => AktionAsync(async () =>
    {
        var anzahl = await Verwaltung.MatrikelnummernErgaenzenAsync(benutzerID, schulID, ComponentToken);
        schuelerDaten = await Verwaltung.SchuelerStammdatenAsync(benutzerID, schulID, ComponentToken);
        matrikelnummer = schuelerDaten.FirstOrDefault(x => x.SchuelerID == schuelerID)?.Matrikelnummer;
        message = $"Matricules attribués : {anzahl}. Les matricules existants ont été conservés.";
    });
    private List<JahresSchuelerZeile> jahresSchueler = [];
    private Guid jahresSchuelerID, jahresKlasseID;
    private string jahresSuche = "";
    private string unterrichtSuche = "";

    private List<JahresSchuelerZeile> GefilterteJahresSchueler => string.IsNullOrWhiteSpace(jahresSuche)
        ? jahresSchueler
        : jahresSchueler.Where(x => x.Name.Contains(jahresSuche.Trim(), StringComparison.CurrentCultureIgnoreCase)
            || x.Klasse.Contains(jahresSuche.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList();

    private IEnumerable<UnterrichtsZeile> GefilterterUnterricht => (uebersicht?.Unterricht ?? [])
        .Where(x => x.SchuljahrID == schuljahrID && (string.IsNullOrWhiteSpace(unterrichtSuche)
            || x.Klasse.Contains(unterrichtSuche.Trim(), StringComparison.CurrentCultureIgnoreCase)
            || x.Fach.Contains(unterrichtSuche.Trim(), StringComparison.CurrentCultureIgnoreCase)
            || x.Lehrer.Contains(unterrichtSuche.Trim(), StringComparison.CurrentCultureIgnoreCase)));
    private bool wiederholer, abschlussBestaetigt;

    private Task WaehleJahrAsync(Guid id) => AktionAsync(async () =>
    {
        schuljahrID = id;
        abschlussBestaetigt = false;
        jahresSchueler = await Verwaltung.JahresSchuelerAsync(benutzerID, schulID, schuljahrID, ComponentToken);
    });

    private Task SpeichereJahresSchuelerAsync() => AktionAsync(async () =>
    {
        await Verwaltung.SchuelerZuordnenAsync(benutzerID, schulID, schuljahrID, jahresKlasseID, jahresSchuelerID, wiederholer, ComponentToken);
        jahresSchueler = await Verwaltung.JahresSchuelerAsync(benutzerID, schulID, schuljahrID, ComponentToken);
        message = "L'inscription annuelle a été enregistrée.";
    });

    private Task SchliesseJahrAsync() => AktionAsync(async () =>
    {
        if (!abschlussBestaetigt) return;
        await Verwaltung.SchuljahrSchliessenAsync(benutzerID, schulID, schuljahrID, ComponentToken);
        abschlussBestaetigt = false;
        await NeuLadenAsync();
        message = "L'année scolaire est clôturée. Les inscriptions sont conservées.";
    });
    private Guid benutzerID, schulID, schuljahrID, klasseID, fachID, lehrerID;
    private string bezeichnung = string.Empty;
    private string message = string.Empty;
    private bool erreur, occupe;
    private decimal wochenstunden = 1;
    private DateOnly beginn = new(DateTime.Today.Year, 9, 1);
    private DateOnly ende = new(DateTime.Today.Year + 1, 7, 31);

    private static List<ModernDropdownOption<Guid>> Options(IEnumerable<AuswahlEintrag> daten)
        => daten.Select(x => new ModernDropdownOption<Guid>(x.Id, x.Bezeichnung)).ToList();
    private List<ModernDropdownOption<Guid>> JahresOptionen => Options(uebersicht?.Schuljahre
        .Where(x => !x.IstGeschlossen).Select(x => new AuswahlEintrag(x.Id, x.Bezeichnung)) ?? []);
    private List<ModernDropdownOption<Guid>> SchuelerOptionen => Options(schuelerDaten
        .Select(x => new AuswahlEintrag(x.SchuelerID, x.Name + " · " + x.Klasse)));

    private void WaehleSchueler(Guid id)
    {
        if (occupe) return;
        schuelerID = id;
        var daten = schuelerDaten.SingleOrDefault(x => x.SchuelerID == id);
        matrikelnummer = daten?.Matrikelnummer;
        geburtsort = daten?.Geburtsort;
        nationalitaet = daten?.Nationalitaet;
        istInterne = daten?.IstInterne ?? false;
        istAffecte = daten?.IstAffecte ?? false;
    }

    private string? nationalitaet;
    private bool istInterne, istAffecte;

    private Task SpeichereDokumentAsync() => AktionAsync(async () =>
    {
        await Verwaltung.DokumentDatenSpeichernAsync(benutzerID, schulID, dokumentDaten, ComponentToken);
        message = "Les coordonnées de l'école ont été enregistrées.";
    });

    private Task SpeichereSchuelerAsync() => AktionAsync(async () =>
    {
        await Verwaltung.SchuelerStammdatenSpeichernAsync(benutzerID, schulID, schuelerID, matrikelnummer, geburtsort, ComponentToken,
            nationalitaet ?? "", istInterne, istAffecte);
        schuelerDaten = await Verwaltung.SchuelerStammdatenAsync(benutzerID, schulID, ComponentToken);
        var daten = schuelerDaten.Single(x => x.SchuelerID == schuelerID);
        matrikelnummer = daten.Matrikelnummer;
        geburtsort = daten.Geburtsort;
        message = "La fiche de l'élève a été enregistrée.";
    });

    protected override async Task OnInitializedAsync()
    {
        await AktionAsync(async () =>
        {
            var state = await Auth.GetAuthenticationStateAsync();
            if (!Guid.TryParse(state.User.FindFirstValue(ClaimTypes.NameIdentifier), out benutzerID))
                throw new UnauthorizedAccessException("Veuillez vous reconnecter.");
            schulen = await Verwaltung.SchulenAsync(benutzerID, ComponentToken);
            schulID = schulen.FirstOrDefault()?.Id ?? Guid.Empty;
            if (schulID != Guid.Empty) await NeuLadenAsync();
        });
    }

    private async Task ChangerEcoleAsync(Guid id)
    {
        if (occupe) return;
        await AktionAsync(async () =>
        {
            schulID = id;
            schuljahrID = klasseID = fachID = lehrerID = Guid.Empty;
            schuelerID = Guid.Empty;
            jahresSchuelerID = jahresKlasseID = Guid.Empty;
            wiederholer = abschlussBestaetigt = false;
            matrikelnummer = geburtsort = null;
            uebersicht = null;
            await NeuLadenAsync();
        });
    }

    private async Task NeuLadenAsync()
    {
        uebersicht = await Verwaltung.LadenAsync(benutzerID, schulID, ComponentToken);
        dokumentDaten = await Verwaltung.DokumentDatenLadenAsync(benutzerID, schulID, ComponentToken);
        schuelerDaten = await Verwaltung.SchuelerStammdatenAsync(benutzerID, schulID, ComponentToken);
        if (!uebersicht.Schuljahre.Any(x => x.Id == schuljahrID && !x.IstGeschlossen))
            schuljahrID = uebersicht.Schuljahre.FirstOrDefault(x => !x.IstGeschlossen)?.Id ?? Guid.Empty;
        jahresSchueler = await Verwaltung.JahresSchuelerAsync(benutzerID, schulID, schuljahrID, ComponentToken);
    }

    private Task SpeichereJahrAsync() => AktionAsync(async () =>
    {
        await Verwaltung.SchuljahrSpeichernAsync(benutzerID, schulID, bezeichnung, beginn, ende, ComponentToken);
        await NeuLadenAsync();
        message = "L'année scolaire a été créée.";
        bezeichnung = string.Empty;
    });

    private Task SpeichereUnterrichtAsync() => AktionAsync(async () =>
    {
        await Verwaltung.UnterrichtSpeichernAsync(benutzerID, schulID, schuljahrID, klasseID, fachID, lehrerID, wochenstunden, ComponentToken);
        await NeuLadenAsync();
        message = "L'affectation a été enregistrée.";
    });

    private Task EntferneAsync(Guid id) => AktionAsync(async () =>
    {
        await Verwaltung.UnterrichtEntfernenAsync(benutzerID, schulID, id, ComponentToken);
        await NeuLadenAsync();
        message = "L'affectation a été retirée.";
    });

    private async Task AktionAsync(Func<Task> action)
    {
        if (occupe) return;
        occupe = true;
        erreur = false;
        message = string.Empty;
        try { await action(); }
        catch (Exception ex) when (IstAbbruch(ex)) { }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            erreur = true;
            message = ex.Message;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unterrichtsverwaltung fehlgeschlagen für Schule {SchulID}", schulID);
            erreur = true;
            message = "Impossible de terminer l'opération. Veuillez réessayer ou contacter l'assistance.";
        }
        finally { occupe = false; }
    }
}
