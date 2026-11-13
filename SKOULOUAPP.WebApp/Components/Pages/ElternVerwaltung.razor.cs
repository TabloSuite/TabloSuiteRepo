using System.Security.Claims;
using SkoulouApp.Core.Models.Import;
using SkoulouApp.WebApp.Components.Shared;
using SkoulouApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace SkoulouApp.WebApp.Components.Pages;

/// <summary>Direktion: Elternkonten anlegen, verknüpfen, trennen und Passwörter neu vergeben.</summary>
public partial class ElternVerwaltung
{
    [Inject] private ElternService Eltern { get; set; } = default!;
    [Inject] private AuthService AuthService { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    private Guid benutzerID;
    private bool laedt = true, fehler;
    private string? meldung, formFehler;
    private string suche = "";
    // Liste in der Scrollbox; angezeigt werden zunächst 50 Einträge („Afficher plus“).
    private const int AnzeigeSchritt = 50;
    private int anzeigeLimit = AnzeigeSchritt;
    private List<SchuelerMitEltern> schueler = [];
    private List<ElternKonto> elternkonten = [];
    private SchuelerMitEltern? ausgewaehlt;
    private bool anlegenOffen, verknuepfenOffen;
    private Formular eingabe = new();
    private Guid verknuepfenElternID;
    private string? verknuepfenBeziehung = "Mère";
    private SchoolUserImportZugang? zugang;

    private static readonly IReadOnlyList<ModernDropdownOption<string?>> BeziehungOptionen =
        [new("Mère", "Mère"), new("Père", "Père"), new("Tuteur", "Tuteur / tutrice"), new("Autre", "Autre")];

    private List<ModernDropdownOption<Guid>> ElternOptionen
        => elternkonten.Select(e => new ModernDropdownOption<Guid>(e.ElternID, $"{e.Name} ({e.Identifiant})", e.Kinder > 0 ? $"{e.Kinder} enfant(s) lié(s)" : null)).ToList();

    private int MitEltern => schueler.Count(s => s.Eltern.Count > 0);

    private IEnumerable<SchuelerMitEltern> Gefiltert => string.IsNullOrWhiteSpace(suche)
        ? schueler
        : schueler.Where(s => s.Name.Contains(suche, StringComparison.CurrentCultureIgnoreCase)
            || s.Klasse.Contains(suche, StringComparison.CurrentCultureIgnoreCase)
            || s.Eltern.Any(e => e.Name.Contains(suche, StringComparison.CurrentCultureIgnoreCase) || e.Identifiant.Contains(suche, StringComparison.OrdinalIgnoreCase)));

    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out benutzerID);
        await AktionAsync(LadenAsync);
    }

    private async Task LadenAsync()
    {
        schueler = await Eltern.UebersichtAsync(benutzerID, ComponentToken);
        elternkonten = await Eltern.ElternkontenAsync(benutzerID, ComponentToken);
    }

    private async Task AktionAsync(Func<Task> aktion)
    {
        laedt = true;
        try { await aktion(); }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { fehler = true; meldung = ex.Message; }
        finally { laedt = false; }
    }

    private Task AnlegenOeffnenAsync(SchuelerMitEltern s)
    {
        ausgewaehlt = s;
        formFehler = null;
        // Vorbelegung aus dem Schülerprofil (Kontakt eines Elternteils).
        eingabe = new Formular { Nachname = s.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "", Telefon = s.ElternTelefon, Email = s.ElternEmail };
        anlegenOffen = true;
        return Task.CompletedTask;
    }

    private void VerknuepfenOeffnen(SchuelerMitEltern s)
    {
        ausgewaehlt = s;
        formFehler = null;
        verknuepfenElternID = Guid.Empty;
        verknuepfenOffen = true;
    }

    private async Task IdentifiantVorschlagenAsync()
        => eingabe.Identifiant = await AuthService.IdentifiantVorschlagAsync(eingabe.Vorname, eingabe.Nachname, ComponentToken);

    private Task AnlegenAsync() => AktionAsync(async () =>
    {
        if (ausgewaehlt is null) return;
        var (ok, text, neuerZugang) = await Eltern.AnlegenAsync(benutzerID, ausgewaehlt.SchuelerID,
            new ElternEingabe(eingabe.Vorname ?? "", eingabe.Nachname ?? "", eingabe.Identifiant, eingabe.Email, eingabe.Telefon, eingabe.Beziehung), ComponentToken);
        if (!ok) { formFehler = text; return; }
        anlegenOffen = false;
        fehler = false; meldung = text;
        zugang = neuerZugang;
        if (!string.IsNullOrWhiteSpace(eingabe.Email) && neuerZugang is not null)
            await AuthService.SendWelcomeEmailAsync(neuerZugang.Identifiant, ComponentToken);
        await LadenAsync();
    });

    private Task VerknuepfenAsync() => AktionAsync(async () =>
    {
        if (ausgewaehlt is null) return;
        try
        {
            await Eltern.VerknuepfenAsync(benutzerID, verknuepfenElternID, ausgewaehlt.SchuelerID, verknuepfenBeziehung, ComponentToken);
        }
        catch (InvalidOperationException ex) { formFehler = ex.Message; return; }
        verknuepfenOffen = false;
        fehler = false; meldung = $"Le parent a été lié à {ausgewaehlt.Name}.";
        await LadenAsync();
    });

    private Task TrennenAsync(SchuelerMitEltern s, ElternVerknuepfung e) => AktionAsync(async () =>
    {
        await Eltern.TrennenAsync(benutzerID, e.ElternID, s.SchuelerID, ComponentToken);
        fehler = false; meldung = $"{e.Name} n'a plus accès aux informations de {s.Name}.";
        await LadenAsync();
    });

    private Task NeuesPasswortAsync(ElternVerknuepfung e) => AktionAsync(async () =>
    {
        zugang = await Eltern.NeuesPasswortAsync(benutzerID, e.ElternID, ComponentToken);
        fehler = false; meldung = $"Un nouveau mot de passe a été créé pour {e.Name}.";
    });

    private sealed class Formular
    {
        public string? Vorname { get; set; }
        public string? Nachname { get; set; }
        public string? Identifiant { get; set; }
        public string? Email { get; set; }
        public string? Telefon { get; set; }
        public string? Beziehung { get; set; } = "Mère";
    }
}
