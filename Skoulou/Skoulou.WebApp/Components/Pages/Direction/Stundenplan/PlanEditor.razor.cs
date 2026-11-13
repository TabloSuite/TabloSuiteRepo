using Microsoft.AspNetCore.Components;
using Skoulou.WebApp.Components.Pages.Organisation;
using Skoulou.WebApp.Components.Shared;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Components.Pages.Direction.Stundenplan;

/// <summary>Bearbeitung eines Stundenplans (lädt nur den Plan neu; die Seite erfährt Änderungen über Rückrufe).</summary>
public partial class PlanEditor
{
    [Inject] private StundenplanService Plaene { get; set; } = default!;

    [Parameter, EditorRequired] public Guid PlanId { get; set; }

    /// <summary>Andere Pläne des Jahres (Vorlagen zum Übernehmen).</summary>
    [Parameter] public IReadOnlyList<AuswahlEintrag> Vorlagen { get; set; } = [];

    /// <summary>Status, Zeitraum oder Stundenzahl hat sich geändert (Planliste der Seite aktualisieren).</summary>
    [Parameter] public EventCallback OnGeaendert { get; set; }

    [Parameter] public EventCallback OnGeloescht { get; set; }

    private PlanDetail? plan;
    private Guid? geladen;
    private bool stundeOffen;
    private Guid? bearbeitetId;
    private StundeEingabe? vorlage;
    private bool datenOffen;
    private bool kopieOffen;
    private Guid? kopieVon;
    private bool loeschenFragen;

    private bool Bearbeitbar => plan is { Veroeffentlicht: false, JahrOffen: true };

    private IReadOnlyList<AuswahlOption<Guid?>> VorlagenOptionen
        => Vorlagen.Where(v => v.Id != PlanId).Select(v => new AuswahlOption<Guid?>(v.Id, v.Name, v.Zusatz)).ToList();

    protected override async Task OnParametersSetAsync()
    {
        if (geladen == PlanId || Laedt) return;
        geladen = PlanId;
        plan = null;
        await AusfuehrenAsync(LadenAsync);
    }

    protected override Task InitialisierenAsync()
    {
        geladen = PlanId;
        return LadenAsync();
    }

    private async Task LadenAsync() => plan = await Plaene.PlanAsync(Ich.KontoId, PlanId);

    private void NeueStunde(FreierPlatz? platz)
    {
        if (plan is null) return;
        var beginn = platz?.Beginn ?? new TimeOnly(7, 0);
        var ende = beginn.Hour < 21 ? beginn.AddHours(1) : new TimeOnly(22, 0);
        vorlage = new StundeEingabe(plan.Lehrauftraege.FirstOrDefault(a => a.Ist < a.Soll)?.Id ?? plan.Lehrauftraege.FirstOrDefault()?.Id ?? Guid.Empty,
            platz?.Tag ?? DayOfWeek.Monday, beginn, ende, plan.KlassenRaum);
        bearbeitetId = null;
        Fehlermeldung = null;
        stundeOffen = true;
    }

    private void FreierPlatzGewaehlt(FreierPlatz platz) => NeueStunde(platz);

    private void StundeBearbeiten(StundeZeile s)
    {
        vorlage = new StundeEingabe(s.LehrauftragId, s.Wochentag, s.Beginn, s.Ende, s.Raum);
        bearbeitetId = s.Id;
        Fehlermeldung = null;
        stundeOffen = true;
    }

    private async Task StundeSpeichernAsync(StundeEingabe e)
    {
        if (plan is null) return;
        var neu = bearbeitetId is null;
        if (await AusfuehrenAsync(() => Plaene.StundeSpeichernAsync(Ich.KontoId, plan.Id, bearbeitetId, e), neu ? "Cours ajouté." : "Cours modifié."))
        {
            stundeOffen = false;
            await AusfuehrenAsync(LadenAsync);
            if (neu) await OnGeaendert.InvokeAsync();
        }
    }

    private async Task StundeLoeschenAsync()
    {
        if (bearbeitetId is not { } id) return;
        if (await AusfuehrenAsync(() => Plaene.StundeLoeschenAsync(Ich.KontoId, id), "Cours retiré."))
        {
            stundeOffen = false;
            await AusfuehrenAsync(LadenAsync);
            await OnGeaendert.InvokeAsync();
        }
    }

    private async Task VeroeffentlichenAsync()
    {
        if (plan is null) return;
        if (await AusfuehrenAsync(() => Plaene.VeroeffentlichenAsync(Ich.KontoId, plan.Id), "Emploi du temps publié."))
        {
            await AusfuehrenAsync(LadenAsync);
            await OnGeaendert.InvokeAsync();
        }
    }

    private async Task ZurueckziehenAsync()
    {
        if (plan is null) return;
        if (await AusfuehrenAsync(() => Plaene.ZurueckziehenAsync(Ich.KontoId, plan.Id), "Publication retirée : vous pouvez modifier le brouillon."))
        {
            await AusfuehrenAsync(LadenAsync);
            await OnGeaendert.InvokeAsync();
        }
    }

    private void DatenOeffnen()
    {
        Fehlermeldung = null;
        datenOffen = true;
    }

    private async Task DatenSpeichernAsync(PlanDialog.Eingabe e)
    {
        if (plan is null) return;
        if (await AusfuehrenAsync(() => Plaene.PlanZeitraumAsync(Ich.KontoId, plan.Id, e.Ab ?? plan.JahrBeginn, e.Bis), "Période de validité enregistrée."))
        {
            datenOffen = false;
            await AusfuehrenAsync(LadenAsync);
            await OnGeaendert.InvokeAsync();
        }
    }

    private void KopieOeffnen()
    {
        Fehlermeldung = null;
        kopieVon = null;
        kopieOffen = true;
    }

    private async Task KopierenAsync()
    {
        if (plan is null || kopieVon is not { } quelle) return;
        (int Kopiert, int Ausgelassen) ergebnis = default;
        if (await AusfuehrenAsync(async () => ergebnis = await Plaene.KopierenAsync(Ich.KontoId, plan.Id, quelle)))
        {
            Toast.ZeigeErfolg(ergebnis.Ausgelassen == 0
                ? $"{ergebnis.Kopiert} cours repris."
                : $"{ergebnis.Kopiert} cours repris ; {ergebnis.Ausgelassen} ignoré(s) faute d'affectation dans cette classe.");
            kopieOffen = false;
            await AusfuehrenAsync(LadenAsync);
            await OnGeaendert.InvokeAsync();
        }
    }

    private async Task PlanLoeschenAsync()
    {
        if (plan is null) return;
        if (await AusfuehrenAsync(() => Plaene.PlanLoeschenAsync(Ich.KontoId, plan.Id), "Brouillon supprimé."))
        {
            loeschenFragen = false;
            await OnGeloescht.InvokeAsync();
        }
    }
}
