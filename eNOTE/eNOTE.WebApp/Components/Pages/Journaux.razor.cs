using ENote.Core.DataModels.Infrastruktur;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;

namespace ENote.WebApp.Components.Pages;

public partial class Journaux(ExceptionLogService logService, ToastService toast)
{
    private List<ExceptionLog> logs = [];
    private bool isLoading = true;
    private DateTime? vonDatum;
    private DateTime? bisDatum;
    private bool nurUnbehandelt;
    private readonly Dictionary<Guid, bool> zeigeDetails = [];

    // Suche
    private string logSuche = string.Empty;

    private IEnumerable<ExceptionLog> GefilterteLogs()
    {
        if (string.IsNullOrWhiteSpace(logSuche))
        {
            return logs;
        }

        var s = logSuche.Trim();
        return logs.Where(l =>
            l.Nachricht.Contains(s, StringComparison.OrdinalIgnoreCase)
            || (l.Pfad?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    protected override async Task OnInitializedAsync() => await LadeAsync();

    private async Task LadeAsync()
    {
        isLoading = true;
        logs = await logService.GetLogsAsync(
            vonDatum,
            bisDatum?.AddDays(1).AddTicks(-1),
            salonId: null,
            nurUnbehandelt: nurUnbehandelt);
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
