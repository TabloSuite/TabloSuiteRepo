using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.Core.Services;

/// <summary>
/// Zentrale Stelle fuer die Frage: Welche Kursniveaus darf ein Schueler
/// ueberhaupt belegen?
///
/// Regel (CECR-basiert):
/// - Die CECR-Gruppe (<see cref="Klasse.CEFRGroup"/>) der aktuellen Klasse des
///   Schuelers bildet die Gruppe. Ein Schueler in einer Klasse mit CECR "A1"
///   darf alle Standardniveaus der Gruppe "A1" (A1.1, A1.2, ...) bearbeiten.
/// - Zusaetzlich sind alle selbst angelegten Klassen der eigenen Schule ohne
///   CECR-Gruppe (z. B. Pruefungsvorbereitung) belegbar.
/// - Normale Schule: Die Klassenstufe (<see cref="KlassenStufe"/>) der aktuellen
///   Klasse bildet die Gruppe, da dort kein CECR gefuehrt wird.
/// - Ist weder CECR noch Klassenstufe hinterlegt (Altdaten), bleibt das
///   bisherige Verhalten ohne Einschraenkung erhalten.
/// </summary>
public class NiveauGruppenService(ISkoulouAppDbContext dbContext)
{
    /// <summary>
    /// Kennzeichnet Klassen ausserhalb des europaeischen Referenzrahmens.
    /// Solche Klassen bilden keine CECR-Gruppe.
    /// </summary>
    public const string OhneCefr = "Hors CECR";

    /// <summary>
    /// Liefert die IDs aller Klassen, die fuer den Schueler zulaessig sind.
    /// Ein Rueckgabewert von <c>null</c> bedeutet "keine Einschraenkung".
    /// </summary>
    public async Task<HashSet<Guid>?> GetErlaubteKlassenIdsAsync(
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        var profil = await dbContext.StudentProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.AppUserId == studentId, cancellationToken);

        if (profil is null)
        {
            return null;
        }

        return await GetErlaubteKlassenIdsAsync(profil, cancellationToken);
    }

    /// <summary>
    /// Ueberladung fuer bereits geladene Profile, um zusaetzliche Abfragen zu vermeiden.
    /// </summary>
    public async Task<HashSet<Guid>?> GetErlaubteKlassenIdsAsync(
        StudentProfile profil,
        CancellationToken cancellationToken = default)
    {
        if (profil.CurrentKlasseId is not Guid klasseId)
        {
            return null;
        }

        var aktuelle = await dbContext.Klassen
            .AsNoTracking()
            .Where(x => x.Id == klasseId)
            .Select(x => new { x.CEFRGroup, x.KlassenStufeId })
            .FirstOrDefaultAsync(cancellationToken);

        if (aktuelle is null)
        {
            return null;
        }

        if (IstEchteCefrGruppe(aktuelle.CEFRGroup))
        {
            var alleKlassen = await dbContext.Klassen
                .AsNoTracking()
                .Select(x => new { x.Id, x.CEFRGroup, x.SchoolId, x.KlassenStufeId })
                .ToListAsync(cancellationToken);

            var erlaubt = alleKlassen
                .Where(x => string.Equals(
                    (x.CEFRGroup ?? string.Empty).Trim(),
                    aktuelle.CEFRGroup!.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Id)
                .ToHashSet();

            // Selbst angelegte Klassen der eigenen Schule ohne CECR-Gruppe
            // (z. B. Pruefungsvorbereitung) bleiben gruppenuebergreifend belegbar.
            foreach (var k in alleKlassen.Where(x => x.SchoolId == profil.SchoolId
                && x.KlassenStufeId == null
                && !IstEchteCefrGruppe(x.CEFRGroup)))
            {
                erlaubt.Add(k.Id);
            }

            return erlaubt.Count == 0 ? null : erlaubt;
        }

        // Normale Schule: Die Klassenstufe der aktuellen Klasse begrenzt die Auswahl.
        if (aktuelle.KlassenStufeId is Guid stufe)
        {
            var ids = await dbContext.Klassen
                .AsNoTracking()
                .Where(x => x.KlassenStufeId == stufe)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            return ids.Count == 0 ? null : ids.ToHashSet();
        }

        return null;
    }

    /// <summary>
    /// Prueft, ob ein konkretes Niveau fuer den Schueler zulaessig ist.
    /// </summary>
    public async Task<bool> IstKlasseErlaubtAsync(
        Guid studentId,
        Guid klasseId,
        CancellationToken cancellationToken = default)
    {
        var erlaubt = await GetErlaubteKlassenIdsAsync(studentId, cancellationToken);
        return erlaubt is null || erlaubt.Contains(klasseId);
    }

    /// <summary>
    /// Prueft, ob ein konkretes Niveau fuer ein bereits geladenes Profil zulaessig ist.
    /// </summary>
    public async Task<bool> IstKlasseErlaubtAsync(
        StudentProfile profil,
        Guid klasseId,
        CancellationToken cancellationToken = default)
    {
        var erlaubt = await GetErlaubteKlassenIdsAsync(profil, cancellationToken);
        return erlaubt is null || erlaubt.Contains(klasseId);
    }

    private static bool IstEchteCefrGruppe(string? cefrGroup)
        => !string.IsNullOrWhiteSpace(cefrGroup)
           && !string.Equals(cefrGroup.Trim(), OhneCefr, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Einheitliche Fehlermeldung fuer abgelehnte Zuweisungen.
    /// </summary>
    public const string NichtErlaubtMeldung =
        "Ce niveau n'appartient pas au groupe CECR actuel de l'élève. "
        + "Veuillez d'abord affecter l'élève à la classe appropriée.";
}
