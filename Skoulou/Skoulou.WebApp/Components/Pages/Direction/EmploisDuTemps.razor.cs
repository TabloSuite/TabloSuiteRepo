using Microsoft.AspNetCore.Components;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Components.Pages.Direction.Stundenplan;
using Skoulou.WebApp.Components.Shared;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Components.Pages.Direction;

/// <summary>Emplois du temps: Klasse wählen, Pläne der Klasse, Bearbeitung im <see cref="PlanEditor"/>.</summary>
public partial class EmploisDuTemps
{
    [Inject] private StundenplanService Plaene { get; set; } = default!;
    [Inject] private KlassenService Klassen { get; set; } = default!;

    /// <summary>Vorgewählte Klasse (?classe=…), z. B. aus der Klassenseite.</summary>
    [SupplyParameterFromQuery(Name = "classe")] public string? KlasseAusAdresseText { get; set; }
    // Kaputte Adresse (?classe=…) brach die Seite mit « Une erreur inattendue » bzw. HTTP 500 ab (Klicktest 21.11.2026) – selbst lesen.
    private Guid? KlasseAusAdresse => Guid.TryParse(KlasseAusAdresseText, out var wert) ? wert : null;

    private IReadOnlyList<AuswahlEintrag>? klassen;
    private IReadOnlyList<AuswahlOption<Guid?>> klassenOptionen = [];
    private Guid? klasseId;
    private IReadOnlyList<PlanZeile>? plaene;
    private Guid? planId;
    private IReadOnlyList<AuswahlEintrag> vorlagen = [];
    private bool neuOffen;

    private bool JahrOffen => Jahr is not null && Jahr.Status != SchuljahrStatus.Abgeschlossen;
    private string? KlassenName => klassen?.FirstOrDefault(k => k.Id == klasseId)?.Name;

    protected override async Task JahrLadenAsync()
    {
        klassen = null;
        plaene = null;
        planId = null;
        vorlagen = [];
        if (JahrId is not { } jahr)
        {
            klasseId = null;
            return;
        }
        klassen = await Klassen.KlassenAuswahlAsync(Ich.KontoId, jahr);
        klassenOptionen = klassen.Select(k => new AuswahlOption<Guid?>(k.Id, k.Name, k.Zusatz)).ToList();
        klasseId = klassen.FirstOrDefault(k => k.Id == (klasseId ?? KlasseAusAdresse))?.Id ?? klassen.FirstOrDefault()?.Id;
        vorlagen = await Plaene.PlaeneDesJahresAsync(Ich.KontoId, jahr);
        await PlaeneLadenAsync(waehlen: true);
    }

    private async Task PlaeneLadenAsync(bool waehlen)
    {
        if (klasseId is not { } id)
        {
            plaene = [];
            planId = null;
            return;
        }
        plaene = await Plaene.PlaeneAsync(Ich.KontoId, id);
        if (waehlen || plaene.All(p => p.Id != planId))
            planId = (plaene.FirstOrDefault(p => p.Veroeffentlicht) ?? plaene.FirstOrDefault())?.Id;
    }

    private async Task KlasseGewaehltAsync(Guid? id)
    {
        if (id == klasseId) return;
        klasseId = id;
        plaene = null;
        planId = null;
        await AusfuehrenAsync(() => PlaeneLadenAsync(waehlen: true));
    }

    private Task NeuOeffnenAsync()
    {
        Fehlermeldung = null;
        neuOffen = true;
        return Task.CompletedTask;
    }

    private async Task AnlegenAsync(PlanDialog.Eingabe e)
    {
        if (klasseId is not { } id) return;
        Guid neu = default;
        if (await AusfuehrenAsync(async () => neu = await Plaene.PlanAnlegenAsync(Ich.KontoId, id, e.Ab, e.Bis, e.VorlageId), "Brouillon créé."))
        {
            neuOffen = false;
            await AusfuehrenAsync(() => PlaeneLadenAsync(waehlen: false));
            planId = neu;
            await VorlagenLadenAsync();
        }
    }

    private async Task PlanGeaendertAsync()
    {
        await AusfuehrenAsync(() => PlaeneLadenAsync(waehlen: false));
        await VorlagenLadenAsync();
    }

    private async Task PlanGeloeschtAsync()
    {
        planId = null;
        await AusfuehrenAsync(() => PlaeneLadenAsync(waehlen: true));
        await VorlagenLadenAsync();
    }

    private async Task VorlagenLadenAsync()
    {
        if (JahrId is { } jahr) await AusfuehrenAsync(async () => vorlagen = await Plaene.PlaeneDesJahresAsync(Ich.KontoId, jahr));
    }
}
