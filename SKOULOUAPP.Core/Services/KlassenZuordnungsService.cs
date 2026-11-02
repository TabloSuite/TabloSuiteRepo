using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Interfaces;

namespace SkoulouApp.Core.Services;

/// <summary>Klassenberechtigungen basieren auf Schulzugehörigkeit, nicht auf Sprachgruppen.</summary>
public sealed class KlassenZuordnungsService(ISkoulouAppDbContext db)
{
    public const string NichtErlaubtMeldung = "Cette classe n'appartient pas à l'école de l'élève.";

    public async Task<bool> IstKlasseErlaubtAsync(Guid studentID, Guid klasseID, CancellationToken token = default)
    {
        var profil = await db.StudentProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.AppUserId == studentID && !x.IsArchived, token);
        return profil is not null && await IstKlasseErlaubtAsync(profil, klasseID, token);
    }

    public Task<bool> IstKlasseErlaubtAsync(StudentProfile profil, Guid klasseID, CancellationToken token = default)
        => db.Klassen.AnyAsync(x => x.Id == klasseID && x.SchoolId == profil.SchoolId && !x.IstSystemNiveau, token);
}
