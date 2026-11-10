using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.WebApp.Services;
using Microsoft.AspNetCore.Components;

namespace SkoulouApp.WebApp.Components.Shared;

public partial class KlassenratErfassung
{
    internal const string Auto = "auto";

    [Inject] private BulletinService Service { get; set; } = default!;

    [Parameter, EditorRequired] public Guid BenutzerID { get; set; }
    [Parameter, EditorRequired] public Guid SchulID { get; set; }
    [Parameter] public IReadOnlyList<AuswahlEintrag> Perioden { get; set; } = [];
    [Parameter] public IReadOnlyList<AuswahlEintrag> Klassen { get; set; } = [];

    private Guid periodeID, klasseID;
    private bool laedt, geladen, fehler;
    private string? meldung;
    private List<Zeile> zeilen = [];

    private List<ModernDropdownOption<Guid>> PeriodenOptionen => Perioden.Select(x => new ModernDropdownOption<Guid>(x.Id, x.Bezeichnung)).ToList();
    private List<ModernDropdownOption<Guid>> KlassenOptionen => Klassen.Select(x => new ModernDropdownOption<Guid>(x.Id, x.Bezeichnung)).ToList();

    private static IReadOnlyList<ModernDropdownOption<string?>> DistinktionOptionen(KlassenratDistinktion vorschlag) =>
    [
        new(Auto, $"Selon la moyenne ({BulletinTexte.DistinktionText(vorschlag) ?? "aucune"})"),
        new(nameof(KlassenratDistinktion.Keine), "Aucune distinction"),
        new(nameof(KlassenratDistinktion.TableauHonneur), "Tableau d'honneur"),
        new(nameof(KlassenratDistinktion.Encouragements), "Tableau d'honneur + Encouragements"),
        new(nameof(KlassenratDistinktion.Felicitations), "Tableau d'honneur + Félicitations")
    ];

    private static readonly IReadOnlyList<ModernDropdownOption<Jahresentscheidung>> EntscheidungOptionen =
    [
        new(Jahresentscheidung.Offen, "Non concernée / à décider"),
        new(Jahresentscheidung.Admis, "Admis(e) en classe supérieure"),
        new(Jahresentscheidung.Redouble, "Redouble la classe"),
        new(Jahresentscheidung.Exclu, "Exclu(e)")
    ];

    protected override void OnParametersSet()
    {
        if (periodeID != Guid.Empty && Perioden.All(x => x.Id != periodeID)) { periodeID = Guid.Empty; zeilen = []; }
        if (klasseID != Guid.Empty && Klassen.All(x => x.Id != klasseID)) { klasseID = Guid.Empty; zeilen = []; }
    }

    private Task PeriodeAsync(Guid id) { periodeID = id; zeilen = []; geladen = false; return Task.CompletedTask; }
    private Task KlasseAsync(Guid id) { klasseID = id; zeilen = []; geladen = false; return Task.CompletedTask; }

    private async Task LadenAsync()
    {
        await AusfuehrenAsync(async () =>
        {
            zeilen = (await Service.KlassenratAsync(BenutzerID, SchulID, periodeID, klasseID, ComponentToken)).Select(Zeile.Aus).ToList();
            geladen = true;
            meldung = null;
        });
    }

    private async Task SpeichernAsync()
    {
        await AusfuehrenAsync(async () =>
        {
            await Service.KlassenratSpeichernAsync(BenutzerID, SchulID, periodeID, zeilen.Select(z => z.Eingabe()).ToList(), ComponentToken);
            meldung = "Les décisions du conseil de classe ont été enregistrées. Elles figureront sur les prochains bulletins.";
        });
    }

    private async Task AusfuehrenAsync(Func<Task> aktion)
    {
        if (laedt) return;
        laedt = true; fehler = false;
        try { await aktion(); }
        catch (InvalidOperationException ex) { fehler = true; meldung = ex.Message; }
        finally { laedt = false; }
    }

    private sealed class Zeile
    {
        public Guid JahresZuordnungID { get; init; }
        public string Schueler { get; init; } = "";
        public decimal? Moyenne { get; init; }
        public int? Rang { get; init; }
        public KlassenratDistinktion Vorschlag { get; init; }
        public string? Distinktion { get; set; } = Auto;
        public KlassenratSanktion Sanktionen { get; set; }
        public string? Bemerkung { get; set; }
        public Jahresentscheidung Entscheidung { get; set; }

        public static Zeile Aus(KlassenratZeile z) => new()
        {
            JahresZuordnungID = z.JahresZuordnungID, Schueler = z.Schueler, Moyenne = z.Moyenne, Rang = z.Rang, Vorschlag = z.Vorschlag,
            Distinktion = z.Distinktion?.ToString() ?? Auto, Sanktionen = z.Sanktionen, Bemerkung = z.Bemerkung, Entscheidung = z.Entscheidung
        };

        public void Sanktion(KlassenratSanktion wert, bool an) => Sanktionen = an ? Sanktionen | wert : Sanktionen & ~wert;

        public KlassenratEingabe Eingabe() => new(JahresZuordnungID,
            Distinktion is null or Auto ? null : Enum.Parse<KlassenratDistinktion>(Distinktion), Sanktionen, Bemerkung, Entscheidung);
    }
}
