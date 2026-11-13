using ENote.Core.DataModels.Infrastruktur;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;

namespace ENote.WebApp.Components.Pages;

public partial class Journaux(ExceptionLogService logService, ToastService toast, LadeZustand ladeZustand)
{
    private List<ExceptionLog> logs = [];
    private bool isLoading = true;
    private DateTime? vonDatum;
    private DateTime? bisDatum;
    private bool nurUnbehandelt;
    private readonly Dictionary<Guid, bool> zeigeDetails = [];

    // Suche
    private string logSuche = string.Empty;

    // Suche läuft in der Datenbank (alle Einträge, nicht nur die geladenen); geladen wird seitenweise.
    private IEnumerable<ExceptionLog> GefilterteLogs() => logs;
    private bool mehr;
    private CancellationTokenSource? suchVerzoegerung;

    private async Task SucheVerzoegertAsync()
    {
        suchVerzoegerung?.Cancel();
        var cts = suchVerzoegerung = new CancellationTokenSource();
        try { await Task.Delay(350, cts.Token); }
        catch (TaskCanceledException) { return; }
        await LadeAsync();
    }

    private async Task MehrLadenAsync()
    {
        var (neu, weitere) = await logService.SucheLogsAsync(vonDatum, bisDatum?.AddDays(1).AddTicks(-1), nurUnbehandelt, logSuche, logs.Count);
        logs.AddRange(neu);
        mehr = weitere;
    }

    protected override async Task OnInitializedAsync()
    {
        using var _ = ladeZustand.Beginnen();
        await LadeAsync();
    }

    private async Task LadeAsync()
    {
        isLoading = true;
        (logs, mehr) = await logService.SucheLogsAsync(
            vonDatum,
            bisDatum?.AddDays(1).AddTicks(-1),
            nurUnbehandelt,
            logSuche,
            0);
        isLoading = false;
    }

    private void ToggleDetails(Guid id) => zeigeDetails[id] = !zeigeDetails.GetValueOrDefault(id);

    private async Task ToggleBehandeltAsync(ExceptionLog log)
    {
        await logService.MarkiereBehandeltAsync(log.Id, !log.IstBehandelt);
        toast.ZeigeInfo(log.IstBehandelt ? "Erreur rouverte." : "Erreur marquée comme traitée.");
        await LadeAsync();
    }

    private async Task LoescheAsync(Guid id)
    {
        await logService.LoescheAsync(id);
        toast.ZeigeErfolg("Entrée supprimée.");
        await LadeAsync();
    }

    private async Task LoescheBehandelteAsync()
    {
        var anzahl = await logService.LoescheBehandelteAsync();
        toast.ZeigeErfolg($"{anzahl} entrée(s) traitée(s) supprimée(s).");
        await LadeAsync();
    }
}
