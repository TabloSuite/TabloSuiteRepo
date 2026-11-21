using Microsoft.AspNetCore.Components;
using Skoulou.WebApp.Components.Pages.Direction.Gemeinsam;
using Skoulou.WebApp.Components.Pages.Direction.SchuelerTeile;
using Skoulou.WebApp.Components.Shared;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Components.Pages.Direction;

/// <summary>Seite « Élèves »: Liste mit Filtern und Auswahl, Dossier, Einschreibungen, Freigabe, Import.</summary>
public partial class Eleves
{
    [SupplyParameterFromQuery(Name = "classe")] public Guid? KlasseAusAdresse { get; set; }
    [SupplyParameterFromQuery(Name = "filtre")] public string? FilterAusAdresse { get; set; }

    [Inject] private SchuljahrService Schuljahre { get; set; } = default!;
    [Inject] private EinschreibungService Einschreibungen { get; set; } = default!;

    private Liste<SchuelerZeile>? liste;
    private SchuelerZaehler? zaehler;
    private List<AuswahlEintrag> klassen = [];
    private DateOnly? jahrBeginn, jahrEnde, vorgabeBeginn;
    private string? filter;
    private Guid? klasseFilter, akteId, bearbeitet;
    private int version;
    private bool dialog, import, ablehnen;
    private SchuelerZeile? kandidat;
    private EinschreibungsAnfrage? einschreibung;
    private KontoAktionAnfrage? aktion;
    private ZugangsStapel? zugaenge;
    private readonly Dictionary<Guid, string> auswahl = [];

    private IReadOnlyList<FilterChip> Chips => zaehler is not { } z ? [] :
    [
        new(null, "Tous", z.Alle), new(SchuelerService.FilterAValider, "À valider", z.AValider),
        new(SchuelerService.FilterSansClasse, "Sans classe", z.SansClasse),
        new(SchuelerService.FilterBloques, "Bloqués", z.Bloques), new(SchuelerService.FilterArchives, "Archivés", z.Archives),
    ];

    private IReadOnlyList<AuswahlOption<Guid?>> KlassenFilterOptionen =>
        [new(null, "Toutes les classes"), .. klassen.Select(k => new AuswahlOption<Guid?>(k.Id, k.Name, k.Zusatz))];

    protected override async Task JahrLadenAsync()
    {
        filter ??= FilterAusAdresse;
        klasseFilter ??= KlasseAusAdresse;
        zaehler = await Schueler.ZaehlerAsync(Ich.KontoId);
        klassen = JahrId is { } id ? (await Klassen.KlassenAuswahlAsync(Ich.KontoId, id)).ToList() : [];
        if (klasseFilter is { } k && klassen.All(x => x.Id != k)) klasseFilter = null;
        var jahr = JahrId is { } jid ? (await Schuljahre.JahreAsync(Ich.KontoId)).FirstOrDefault(j => j.Id == jid) : null;
        (jahrBeginn, jahrEnde) = (jahr?.Beginn, jahr?.Ende);
        // Vorgabe « Inscrit(e) depuis le »: Schuljahresbeginn nur in der Startphase einer neuen Schule, sonst heute (21.11.2026).
        vorgabeBeginn = JahrId is { } vj ? await Einschreibungen.StandardBeginnAsync(Ich.KontoId, vj) : null;
        if (liste is not null) await liste.NeuLadenAsync();
    }

    private async Task<(IReadOnlyList<SchuelerZeile>, bool)> LadenAsync(ListenAnfrage a, CancellationToken ct)
    {
        filter = a.Filter;
        return (await Schueler.SucheAsync(Ich.KontoId, a.Filter, klasseFilter, a.Suche, a.Ueberspringen, a.Anzahl, ct)).AlsTupel();
    }

    private async Task KlasseFilterAsync(Guid? id)
    {
        klasseFilter = id;
        if (liste is not null) await liste.NeuLadenAsync();
    }

    /// <summary>Die Liste übernimmt den Anfangsfilter nur beim Erzeugen – neuer Schlüssel = neue Liste mit diesem Filter.</summary>
    private void FilterAsync(string wert)
    {
        filter = wert;
        listenSchluessel++;
    }

    private int listenSchluessel;

    private void Waehlen(SchuelerZeile s, ChangeEventArgs e)
    {
        if (e.Value is true) auswahl[s.KontoId] = s.Name;
        else auswahl.Remove(s.KontoId);
    }

    private void Bearbeiten(Guid? id) => (bearbeitet, dialog) = (id, true);

    private void Validieren(SchuelerZeile s, bool ablehnenWahl) => (kandidat, ablehnen) = (s, ablehnenWahl);

    private void SammelEinschreiben() => einschreibung = new(EinschreibungsAktion.Sammel, auswahl.Keys.ToList(), auswahl.Values.ToList());

    private void SammelPasswort() => aktion = new KontoAktionAnfrage(KontoAktion.Passwort, auswahl.Keys.ToList(), auswahl.Values.ToList());

    /// <summary>Nach jeder Änderung: Zähler, Liste und (falls offen) Dossier neu laden.</summary>
    private async Task ListeLadenAsync()
    {
        await AusfuehrenAsync(async () =>
        {
            zaehler = await Schueler.ZaehlerAsync(Ich.KontoId);
            if (JahrId is { } id) klassen = (await Klassen.KlassenAuswahlAsync(Ich.KontoId, id)).ToList();
        });
        if (liste is not null) await liste.NeuLadenAsync();
    }

    private async Task GespeichertAsync(ZugangsStapel? stapel)
    {
        dialog = false;
        zugaenge = stapel;
        version++;
        await ListeLadenAsync();
    }

    private async Task ErledigtAsync()
    {
        (einschreibung, kandidat) = (null, null);
        auswahl.Clear();
        version++;
        await ListeLadenAsync();
    }

    private async Task AktionErledigtAsync(ZugangsStapel? stapel)
    {
        aktion = null;
        zugaenge = stapel;
        if (stapel is not null) auswahl.Clear();
        version++;
        await ListeLadenAsync();
    }

    private async Task ImportiertAsync(ZugangsStapel stapel)
    {
        import = false;
        zugaenge = stapel;
        await ListeLadenAsync();
    }
}
