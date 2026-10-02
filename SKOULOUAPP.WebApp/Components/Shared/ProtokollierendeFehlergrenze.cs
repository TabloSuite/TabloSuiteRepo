using Microsoft.AspNetCore.Components.Web;

namespace SkoulouApp.WebApp.Components.Shared;

public sealed class ProtokollierendeFehlergrenze(ILogger<ProtokollierendeFehlergrenze> logger) : ErrorBoundary
{
    protected override Task OnErrorAsync(Exception exception)
    {
        logger.LogError(exception, "Unbehandelter Fehler bei der Darstellung einer Blazor-Seite.");
        return Task.CompletedTask;
    }
}
