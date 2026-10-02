using SkoulouApp.Core.DataModels;
using SkoulouApp.Core.Interfaces;

namespace SkoulouApp.Core.Services;

public class SchuelerProfilService : ISchuelerProfilService
{
    private readonly List<SchuelerProfilModell> _schuelerProfile = [];

    public Task<SchuelerProfilModell?> HoleNachBenutzerIDAsync(Guid benutzerID, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var schuelerProfil = _schuelerProfile.FirstOrDefault(profil => profil.BenutzerID == benutzerID);

        return Task.FromResult(schuelerProfil);
    }

    public Task<IReadOnlyList<SchuelerProfilModell>> HoleNachSchulIDAsync(Guid schulID, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<SchuelerProfilModell> schuelerProfile = _schuelerProfile
            .Where(profil => profil.SchulID == schulID)
            .OrderBy(profil => profil.Anzeigename)
            .ToList();

        return Task.FromResult(schuelerProfile);
    }

    public Task<SchuelerProfilModell> SpeichernAsync(SchuelerProfilModell schuelerProfil, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (schuelerProfil.Id == Guid.Empty)
        {
            schuelerProfil.Id = Guid.NewGuid();
            schuelerProfil.ErstelltAmUtc = DateTime.UtcNow;
        }

        var vorhandenesProfil = _schuelerProfile.FirstOrDefault(profil => profil.Id == schuelerProfil.Id);
        if (vorhandenesProfil is null)
        {
            _schuelerProfile.Add(schuelerProfil);
        }
        else
        {
            var index = _schuelerProfile.IndexOf(vorhandenesProfil);
            schuelerProfil.GeaendertAmUtc = DateTime.UtcNow;
            _schuelerProfile[index] = schuelerProfil;
        }

        return Task.FromResult(schuelerProfil);
    }
}

