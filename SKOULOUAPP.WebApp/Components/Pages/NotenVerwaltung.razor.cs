using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.Core.Services;
using SkoulouApp.WebApp.Components.Shared;
using SkoulouApp.WebApp.Data;
using SkoulouApp.WebApp.Services;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class NotenVerwaltung
{
    [Inject] private BewertungsService Service { get; set; } = default!;
    [Inject] private UnterrichtsVerwaltungService Verwaltung { get; set; } = default!;
    [Inject] private IDbContextFactory<ApplicationDbContext> Factory { get; set; } = default!;
    [Inject] private AuthenticationStateProvider Auth { get; set; } = default!;
    [Inject] private ILogger<NotenVerwaltung> Logger { get; set; } = default!;
    private List<AuswahlEintrag> schulen = [], jahre = [];
    private UnterrichtsUebersicht? unterricht;
    private BewertungsUebersicht? daten;
    private Guid userID, schulID, jahrID, periodeID, klasseID, fachID, bewertungsFachID, bestandteilID, schuelerID;
    private bool verwalter, occupe;
    private int art, reihenfolge = 1;
    private decimal gewicht = 1, koeffizient = 1, komponentenGewicht = 1, maximum = 20, punkte;
    private string message = "", titel = "", komponente = "";
    private string? bemerkung;
    private List<NotenAenderung> historie = [];
    private Task HistorieAsync() => AktionAsync(async () => historie = await Service.HistorieAsync(userID, bestandteilID, bewertungsFachID, schuelerID, ComponentToken));
    private static List<ModernDropdownOption<Guid>> Options(IEnumerable<AuswahlEintrag> items)
        => items.Select(x => new ModernDropdownOption<Guid>(x.Id, x.Bezeichnung)).ToList();
    private static readonly List<ModernDropdownOption<int>> Arten = [new(0, "Bulletin général"), new(1, "Trimestre"), new(2, "Examen blanc / test")];
    private List<ModernDropdownOption<Guid>> PeriodenOptionen => Options(daten?.Perioden.Select(x => new AuswahlEintrag(x.Id, x.Titel)) ?? []);
    private List<ModernDropdownOption<Guid>> FachOptionen => Options(daten?.Faecher.Select(x => new AuswahlEintrag(x.Id, $"{x.Klasse} · {x.Fach} · Coef. {x.Koeffizient}")) ?? []);
    private List<ModernDropdownOption<Guid>> BestandteilOptionen => Options(daten?.Bestandteile.Where(x => x.PeriodeID == periodeID)
        .Select(x => new AuswahlEintrag(x.Id, $"{x.Bezeichnung} / {x.MaximalPunkte} · Poids {x.Gewicht}")) ?? []);
    private List<ModernDropdownOption<Guid>> SchuelerOptionen => Options(daten?.Schueler
        .Where(x => x.KlasseID == daten.Faecher.SingleOrDefault(f => f.Id == bewertungsFachID)?.KlasseID)
        .Select(x => new AuswahlEintrag(x.Id, x.Name)) ?? []);
    private IEnumerable<(string Name, string Ergebnis)> Resultate
    {
        get
        {
            if (daten is null) yield break;
            var fach = daten.Faecher.SingleOrDefault(x => x.Id == bewertungsFachID);
            foreach (var schueler in daten.Schueler.Where(x => x.KlasseID == fach?.KlasseID))
            {
                var moyenne = NotenBerechnung.FachMittel(daten.Bestandteile.Where(x => x.PeriodeID == periodeID)
                    .Select(x => new NotenAnteil(daten.Noten.SingleOrDefault(n => n.BestandteilID == x.Id
                        && n.FachBewertungID == bewertungsFachID && n.JahresZuordnungID == schueler.Id)?.Punkte, x.MaximalPunkte, x.Gewicht)));
                yield return (schueler.Name, moyenne is null ? "Notes incomplètes" : $"Moyenne : {NotenBerechnung.Anzeige(moyenne.Value):0.00} / 20");
            }
        }
    }
    private void WaehleFach(Guid id) { historie = []; bewertungsFachID = id; schuelerID = Guid.Empty; punkte = 0; bemerkung = null; }
    private void WaehlePeriode(Guid id) { historie = []; periodeID = id; bestandteilID = Guid.Empty; punkte = 0; bemerkung = null; }
    private void WaehleBestandteil(Guid id) { bestandteilID = id; LadeNote(); }
    private void WaehleSchueler(Guid id) { schuelerID = id; LadeNote(); }
    private void LadeNote()
    {
        historie = [];
        var note = daten?.Noten.SingleOrDefault(x => x.BestandteilID == bestandteilID && x.FachBewertungID == bewertungsFachID && x.JahresZuordnungID == schuelerID);
        punkte = note?.Punkte ?? 0; bemerkung = note?.Bemerkung;
    }
    protected override Task OnInitializedAsync() => AktionAsync(async () =>
    {
        var state = await Auth.GetAuthenticationStateAsync();
        if (!Guid.TryParse(state.User.FindFirstValue(ClaimTypes.NameIdentifier), out userID)) throw new UnauthorizedAccessException("Veuillez vous reconnecter.");
        verwalter = state.User.IsInRole(Rollen.SchulLeiter) || state.User.IsInRole(Rollen.Koordinator);
        await using var db = await Factory.CreateDbContextAsync(ComponentToken);
        schulen = await db.UserSchools.AsNoTracking().Where(x => x.AppUserId == userID && x.School.IsActive)
            .Select(x => new AuswahlEintrag(x.SchoolId, x.School.Name)).ToListAsync(ComponentToken);
        schulID = schulen.FirstOrDefault()?.Id ?? Guid.Empty;
        if (schulID != Guid.Empty) await SchuleLadenAsync();
    });
    private Task SchuleAsync(Guid id) => AktionAsync(async () => { schulID = id; await SchuleLadenAsync(); });
    private async Task SchuleLadenAsync()
    {
        daten = null; periodeID = bestandteilID = bewertungsFachID = schuelerID = klasseID = fachID = Guid.Empty;
        await using var db = await Factory.CreateDbContextAsync(ComponentToken);
        jahre = await db.Set<Schuljahr>().AsNoTracking().Where(x => x.SchulID == schulID).OrderByDescending(x => x.Beginn)
            .Select(x => new AuswahlEintrag(x.Id, x.Bezeichnung)).ToListAsync(ComponentToken);
        jahrID = jahre.FirstOrDefault()?.Id ?? Guid.Empty;
        unterricht = verwalter ? await Verwaltung.LadenAsync(userID, schulID, ComponentToken) : null;
        await LadenAsync();
    }
    private Task JahrAsync(Guid id) => AktionAsync(async () => { jahrID = id; periodeID = bestandteilID = bewertungsFachID = schuelerID = Guid.Empty; await LadenAsync(); });
    private async Task LadenAsync()
    {
        historie = [];
        if (jahrID == Guid.Empty) { daten = null; return; }
        daten = await Service.LadenAsync(userID, schulID, jahrID, ComponentToken);
        if (!daten.Perioden.Any(x => x.Id == periodeID)) periodeID = daten.Perioden.FirstOrDefault()?.Id ?? Guid.Empty;
    }
    private Task PeriodeAsync() => AktionAsync(async () => { periodeID = await Service.PeriodeAnlegenAsync(userID, schulID, jahrID, titel, (BewertungsArt)art, reihenfolge, gewicht, ComponentToken); await LadenAsync(); });
    private Task FachAsync() => AktionAsync(async () => { bewertungsFachID = await Service.FachFestlegenAsync(userID, schulID, jahrID, klasseID, fachID, koeffizient, ComponentToken); await LadenAsync(); });
    private Task KomponenteAsync() => AktionAsync(async () => { bestandteilID = await Service.BestandteilAnlegenAsync(userID, periodeID, komponente, komponentenGewicht, maximum, ComponentToken); await LadenAsync(); });
    private Task NoteAsync() => AktionAsync(async () => { await Service.NoteSpeichernAsync(userID, bestandteilID, bewertungsFachID, schuelerID, punkte, bemerkung, ComponentToken); await LadenAsync(); message = "La note a été enregistrée."; });
    private Task ClotureAsync() => AktionAsync(async () => {
        var periode = daten?.Perioden.SingleOrDefault(x => x.Id == periodeID);
        if (periode is null) return;
        await Service.PeriodeSchliessenAsync(userID, periodeID, !periode.IstGeschlossen, ComponentToken); await LadenAsync();
        message = periode.IstGeschlossen ? "La période a été rouverte. Les bulletins publiés restent protégés." : "La période a été clôturée : la saisie est verrouillée.";
    });
    private async Task AktionAsync(Func<Task> action)
    {
        if (occupe) return; occupe = true; message = "";
        try { await action(); }
        catch (Exception ex) when (IstAbbruch(ex)) { }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { message = ex.Message; }
        catch (Exception ex) { Logger.LogError(ex, "Notenverwaltung für Schule {SchulID}", schulID); message = "Impossible de terminer l'opération. Veuillez réessayer."; }
        finally { occupe = false; }
    }
}
