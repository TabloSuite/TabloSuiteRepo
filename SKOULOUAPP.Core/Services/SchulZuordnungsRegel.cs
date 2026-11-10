using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.Core.Services;

/// <summary>
/// Verbindliche Regel (seit 10.11.2026): Ein Konto gehört zu genau einer Schule.
/// Gilt für SchulLeiter, Koordinator, Lehrer und Schüler. Eine Lehrkraft an zwei Schulen
/// bekommt pro Schule ein eigenes Konto. SuperAdmin und ProduktOwner sind Plattformrollen:
/// SuperAdmin sieht alle Schulen, ProduktOwner betreuen ihnen zugewiesene Schulen.
/// Der eindeutige Datenbankindex <c>IX_UserSchools_EineSchuleProKonto</c> sichert die Regel zusätzlich ab.
/// </summary>
public static class SchulZuordnungsRegel
{
    public const string MeldungGenauEineSchule = "Un compte appartient à un seul établissement. Choisissez un établissement.";
    public const string MeldungSchuleFest = "Ce compte appartient déjà à un autre établissement. Pour cet établissement, créez un nouveau compte.";
    public const string MeldungKeineWeitereSchule = "Chaque compte de direction gère un seul établissement. Pour un autre établissement, créez un nouveau compte.";

    /// <summary>Rollen, die mehrere Schulen sehen bzw. betreuen dürfen.</summary>
    public static bool IstPlattformRolle(string? rolle) => rolle is Rollen.SuperAdmin or Rollen.ProduktOwner;

    /// <summary>Plattformzuordnung (mehrfach erlaubt) nur für ProduktOwner.</summary>
    public static bool IstPlattformZuordnung(string? rolle) => rolle == Rollen.ProduktOwner;

    /// <summary>
    /// Prüft die gewünschte Schulzuordnung eines Kontos. Liefert <c>null</c>, wenn sie zulässig ist,
    /// sonst eine französische Meldung für die Oberfläche.
    /// </summary>
    public static async Task<string?> PruefenAsync(
        ISkoulouAppDbContext dbContext,
        Guid? kontoID,
        string rolle,
        IReadOnlyCollection<Guid> schulIDs,
        CancellationToken cancellationToken = default)
    {
        if (IstPlattformRolle(rolle))
            return null;
        if (schulIDs.Count != 1)
            return MeldungGenauEineSchule;
        if (kontoID is not Guid id)
            return null;

        var schulID = schulIDs.First();
        var andereSchule = await dbContext.UserSchools.AsNoTracking()
            .AnyAsync(link => link.AppUserId == id && !link.IstPlattformZuordnung && link.SchoolId != schulID, cancellationToken)
            || await dbContext.StudentProfiles.AsNoTracking()
                .AnyAsync(profil => profil.AppUserId == id && profil.SchoolId != schulID, cancellationToken);
        return andereSchule ? MeldungSchuleFest : null;
    }
}
