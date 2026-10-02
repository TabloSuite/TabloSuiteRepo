using SkoulouApp.Core.DataModels;

namespace SkoulouApp.Core.Interfaces;

public interface ISchuelerProfilService
{
    Task<SchuelerProfilModell?> HoleNachBenutzerIDAsync(Guid benutzerID, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SchuelerProfilModell>> HoleNachSchulIDAsync(Guid schulID, CancellationToken cancellationToken = default);
    Task<SchuelerProfilModell> SpeichernAsync(SchuelerProfilModell schuelerProfil, CancellationToken cancellationToken = default);
}

