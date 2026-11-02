using SkoulouApp.Core.Services;

namespace SkoulouApp.Core.Interfaces;

/// <summary>Ein eigener Context und Identity-Store je atomarem Benutzerarbeitsvorgang.</summary>
public interface IBenutzerArbeitsvorgang
{
    Task<T> AusfuehrenAsync<T>(Func<DashboardService, SchoolUserImportService, Task<T>> aktion,
        Func<T, bool> erfolgreich, CancellationToken token);
}
