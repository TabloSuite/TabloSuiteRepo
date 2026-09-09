using HalloApp.Core.DataModels;

namespace HalloApp.Core.Interfaces;

public interface ILektionsFortschrittService
{
    Task<LektionsFortschrittModell?> HoleNachSchuelerUndLektionAsync(Guid schuelerID, Guid lektionID, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LektionsFortschrittModell>> HoleNachSchuelerIDAsync(Guid schuelerID, CancellationToken cancellationToken = default);
    Task<LektionsFortschrittModell> AlsAbgeschlossenMarkierenAsync(Guid schuelerID, Guid lektionID, CancellationToken cancellationToken = default);
    Task<LektionsFortschrittModell> SpeichernAsync(LektionsFortschrittModell fortschritt, CancellationToken cancellationToken = default);
}

