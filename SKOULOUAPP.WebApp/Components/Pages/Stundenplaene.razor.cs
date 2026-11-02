using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.WebApp.Components.Shared;
using SkoulouApp.WebApp.Data;
using SkoulouApp.WebApp.Services;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class Stundenplaene
{
    [Inject] private StundenplanService Service { get; set; } = default!;
    [Inject] private UnterrichtsVerwaltungService Verwaltung { get; set; } = default!;
    [Inject] private IDbContextFactory<ApplicationDbContext> Factory { get; set; } = default!;
    [Inject] private AuthenticationStateProvider Auth { get; set; } = default!;
    [Inject] private ILogger<Stundenplaene> Logger { get; set; } = default!;
    private List<AuswahlEintrag> schulen = [];
    private List<Stundenplan> plaene = [];
    private List<StundenplanZeile> zeilen = [];
    private List<WochenstundenZeile> stunden = [];
    private int rasterMinuten = 60;
    private TimeOnly rasterBeginn = new(7, 0), rasterEnde = new(17, 0);
    private List<ModernDropdownOption<int>> RasterOptionen => rasterMinuten is < 5 or > 180 || rasterBeginn >= rasterEnde ? [] :
        Enumerable.Range(0, (int)(rasterEnde - rasterBeginn).TotalMinutes / rasterMinuten)
            .Select(x => new ModernDropdownOption<int>(x, $"{rasterBeginn.AddMinutes(x * rasterMinuten):HH:mm} – {rasterBeginn.AddMinutes((x + 1) * rasterMinuten):HH:mm}")).ToList();
    private int rasterIndex;
    private void RasterWaehlen(int index) { rasterIndex = index; beginn = rasterBeginn.AddMinutes(index * rasterMinuten); ende = beginn.AddMinutes(rasterMinuten); }
    private Task PlanWaehlenAsync(Guid id) => AktionAsync(async () => { planID = id; unterrichtID = Guid.Empty;
        stunden = await Service.WochenstundenAsync(benutzerID, planID, ComponentToken); });
    private Task KopierenAsync() => AktionAsync(async () => { planID = await Service.KopierenAsync(benutzerID, planID, titel, ab, bis, ComponentToken); await LadenAsync(); message = "La copie a été créée comme brouillon. Vérifiez les conflits avant de la publier."; });
    private UnterrichtsUebersicht? uebersicht;
    private Guid benutzerID, schulID, jahrID, klasseID, planID, unterrichtID;
    private bool verwalter, occupe;
    private string message = "", titel = "", suche = "";
    private string? raum;
    private DateOnly ab = DateOnly.FromDateTime(DateTime.Today), bis = DateOnly.FromDateTime(DateTime.Today.AddMonths(3));
    private TimeOnly beginn = new(7, 0), ende = new(8, 0);
    private int wochentag = 1;
    private Stundenplan? AktuellerPlan => plaene.SingleOrDefault(x => x.Id == planID);
    private List<ModernDropdownOption<Guid>> PlanOptionen => Options(plaene.Select(x =>
        new AuswahlEintrag(x.Id, x.Bezeichnung + (x.IstVeroeffentlicht ? " · Publié" : " · Brouillon"))));
    private IEnumerable<StundenplanZeile> Gefiltert => zeilen.Where(x => (!verwalter || planID == Guid.Empty || x.PlanID == planID)
        && (string.IsNullOrWhiteSpace(suche) || $"{x.Klasse} {x.Fach} {x.Lehrer}".Contains(suche, StringComparison.CurrentCultureIgnoreCase)));
    private static List<ModernDropdownOption<Guid>> Options(IEnumerable<AuswahlEintrag> daten)
        => daten.Select(x => new ModernDropdownOption<Guid>(x.Id, x.Bezeichnung)).ToList();
    private List<ModernDropdownOption<Guid>> UnterrichtOptionen => Options(uebersicht?.Unterricht
        .Where(x => x.SchuljahrID == AktuellerPlan?.SchuljahrID && x.KlasseID == AktuellerPlan?.KlasseID)
        .Select(x => new AuswahlEintrag(x.Id, x.Fach + " · " + x.Lehrer)) ?? []);
    private static string TagName(DayOfWeek tag) => new System.Globalization.CultureInfo("fr-FR").DateTimeFormat.GetDayName(tag);
    private static readonly List<ModernDropdownOption<int>> Tage = Enumerable.Range(1, 7)
        .Select(x => new ModernDropdownOption<int>(x % 7, TagName((DayOfWeek)(x % 7)))).ToList();

    protected override Task OnInitializedAsync() => AktionAsync(async () =>
    {
        var state = await Auth.GetAuthenticationStateAsync();
        if (!Guid.TryParse(state.User.FindFirstValue(ClaimTypes.NameIdentifier), out benutzerID)) throw new UnauthorizedAccessException("Veuillez vous reconnecter.");
        verwalter = state.User.IsInRole(Rollen.SchulLeiter) || state.User.IsInRole(Rollen.Koordinator);
        if (verwalter) schulen = await Verwaltung.SchulenAsync(benutzerID, ComponentToken);
        else
        {
            await using var db = await Factory.CreateDbContextAsync(ComponentToken);
            schulen = await db.UserSchools.AsNoTracking().Where(x => x.AppUserId == benutzerID && x.School.IsActive)
                .Select(x => new AuswahlEintrag(x.SchoolId, x.School.Name)).ToListAsync(ComponentToken);
        }
        schulID = schulen.FirstOrDefault()?.Id ?? Guid.Empty;
        if (schulID != Guid.Empty) await LadenAsync();
    });
    private Task EcoleAsync(Guid id) => AktionAsync(async () => { schulID = id; planID = unterrichtID = Guid.Empty; await LadenAsync(); });
    private async Task LadenAsync()
    {
        zeilen = await Service.LesenAsync(benutzerID, schulID, ComponentToken);
        if (verwalter)
        {
            uebersicht = await Verwaltung.LadenAsync(benutzerID, schulID, ComponentToken);
            plaene = await Service.PlaeneAsync(benutzerID, schulID, ComponentToken);
            if (!plaene.Any(x => x.Id == planID)) planID = plaene.FirstOrDefault()?.Id ?? Guid.Empty;
            stunden = planID == Guid.Empty ? [] : await Service.WochenstundenAsync(benutzerID, planID, ComponentToken);
        }
    }
    private Task AnlegenAsync() => AktionAsync(async () => { planID = await Service.AnlegenAsync(benutzerID, schulID, jahrID, klasseID, titel, ab, bis, ComponentToken); await LadenAsync(); });
    private Task EintragAsync() => AktionAsync(async () => { await Service.EintragSpeichernAsync(benutzerID, planID, unterrichtID, (DayOfWeek)wochentag, beginn, ende, raum, ComponentToken); await LadenAsync(); });
    private Task PublierAsync() => AktionAsync(async () => { if (AktuellerPlan is not { } p) return; await Service.VeroeffentlichenAsync(benutzerID, p.Id, !p.IstVeroeffentlicht, ComponentToken); await LadenAsync(); });
    private Task RetirerAsync(Guid id) => AktionAsync(async () => { await Service.EntfernenAsync(benutzerID, id, ComponentToken); await LadenAsync(); });
    private async Task AktionAsync(Func<Task> action)
    {
        if (occupe) return;
        occupe = true; message = "";
        try { await action(); }
        catch (Exception ex) when (IstAbbruch(ex)) { }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { message = ex.Message; }
        catch (Exception ex) { Logger.LogError(ex, "Stundenplanfehler für Schule {SchulID}", schulID); message = "Impossible de terminer l'opération. Veuillez réessayer."; }
        finally { occupe = false; }
    }
}
