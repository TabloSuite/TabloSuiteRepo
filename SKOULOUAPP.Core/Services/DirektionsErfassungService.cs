using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.Interfaces;
using SkoulouApp.Core.Models.TeacherLessons;

namespace SkoulouApp.Core.Services;

/// <summary>
/// Erfassung durch die Direktion: Viele Lehrkräfte haben kein Gerät. Schulleitung
/// (<see cref="Rollen.SchulLeiter"/>) und Koordination (<see cref="Rollen.Koordinator"/>)
/// dürfen deshalb Lektionen, Lektionsübungen und Prüfungen im Namen eines Lehrers
/// ihrer eigenen Schule anlegen, bearbeiten, veröffentlichen und löschen – ohne
/// Bestätigung durch den Lehrer. Solche Inhalte tragen <c>ErfasstVonId</c> und werden
/// mit « Saisi par la direction » gekennzeichnet.
/// Jede Schreibaktion prüft die Berechtigung serverseitig erneut, weil in Blazor
/// gebundene Werte (z. B. die Lehrerauswahl) vom Client stammen.
/// </summary>
public sealed class DirektionsErfassungService(ISkoulouAppDbContext db)
{
    /// <summary>Anzeigetext der Kennzeichnung.</summary>
    public const string Kennzeichnung = "Saisi par la direction";

    public const string KeineBerechtigungHinweis = "Vous n'êtes pas autorisé à saisir des contenus pour cet enseignant.";

    /// <summary>
    /// <c>true</c>, wenn <paramref name="handelnderId"/> selbst der Lehrer ist, oder wenn der
    /// Handelnde nicht gesperrt ist, Schulleitung/Koordination ist und mit dem Lehrer eine
    /// freigegebene Schule teilt; der Lehrer muss die Rolle Lehrer haben und darf nicht
    /// gesperrt sein.
    /// </summary>
    public async Task<bool> DarfFuerLehrerHandelnAsync(Guid handelnderId, Guid lehrerId, CancellationToken token = default)
    {
        if (handelnderId == Guid.Empty || lehrerId == Guid.Empty)
        {
            return false;
        }

        if (handelnderId == lehrerId)
        {
            return true;
        }

        if (!await IstAktiveDirektionAsync(handelnderId, token))
        {
            return false;
        }

        var istAktiverLehrer = await db.Users.AsNoTracking()
            .AnyAsync(user => user.Id == lehrerId && !user.IsBlocked
                && db.UserRoles.Any(ur => ur.UserId == user.Id
                    && db.Roles.Any(role => role.Id == ur.RoleId && role.Name == Rollen.Lehrer)), token);
        if (!istAktiverLehrer)
        {
            return false;
        }

        var freieSchulen = new SchulZugangService(db).FreigegebeneSchulen();
        return await db.UserSchools.AsNoTracking()
            .AnyAsync(direktion => direktion.AppUserId == handelnderId
                && !direktion.IstPlattformZuordnung
                && freieSchulen.Contains(direktion.SchoolId)
                && db.UserSchools.Any(lehrer => lehrer.AppUserId == lehrerId
                    && !lehrer.IstPlattformZuordnung
                    && lehrer.SchoolId == direktion.SchoolId), token);
    }

    /// <summary>
    /// Wirft <see cref="UnauthorizedAccessException"/>, wenn <paramref name="handelnderId"/>
    /// nicht im Namen von <paramref name="lehrerId"/> handeln darf.
    /// </summary>
    public async Task PruefeAsync(Guid handelnderId, Guid lehrerId, CancellationToken token = default)
    {
        if (!await DarfFuerLehrerHandelnAsync(handelnderId, lehrerId, token))
        {
            throw new UnauthorizedAccessException(KeineBerechtigungHinweis);
        }
    }

    /// <summary>
    /// Prüft eine Schreibaktion im Namen von <paramref name="lehrerId"/> und liefert die
    /// ID für <c>ErfasstVonId</c>: der Handelnde, wenn er nicht selbst der Lehrer ist,
    /// sonst <c>null</c> (der Lehrer arbeitet selbst).
    /// </summary>
    public async Task<Guid?> PruefeUndErmittleErfasserAsync(Guid? handelnderId, Guid lehrerId, CancellationToken token = default)
    {
        if (handelnderId is not Guid handelnder || handelnder == lehrerId)
        {
            return null;
        }

        await PruefeAsync(handelnder, lehrerId, token);
        return handelnder;
    }

    /// <summary>
    /// Lehrkräfte (Rolle Lehrer, nicht gesperrt) aus den freigegebenen Schulen der
    /// Direktion. Leer, wenn der Handelnde keine aktive Schulleitung/Koordination ist.
    /// </summary>
    public async Task<IReadOnlyList<LehrerAuswahlItem>> GetLehrerFuerDirektionAsync(Guid handelnderId, CancellationToken token = default)
    {
        if (!await IstAktiveDirektionAsync(handelnderId, token))
        {
            return [];
        }

        var freieSchulen = new SchulZugangService(db).FreigegebeneSchulen();
        var schulIds = db.UserSchools.AsNoTracking()
            .Where(link => link.AppUserId == handelnderId && !link.IstPlattformZuordnung && freieSchulen.Contains(link.SchoolId))
            .Select(link => link.SchoolId);

        var lehrer = await db.Users.AsNoTracking()
            .Where(user => !user.IsBlocked
                && db.UserRoles.Any(ur => ur.UserId == user.Id
                    && db.Roles.Any(role => role.Id == ur.RoleId && role.Name == Rollen.Lehrer))
                && db.UserSchools.Any(link => link.AppUserId == user.Id
                    && !link.IstPlattformZuordnung
                    && schulIds.Contains(link.SchoolId)))
            .Select(user => new { user.Id, user.FullName, user.Vorname, user.Nachname, user.UserName, user.Email })
            .ToListAsync(token);

        return lehrer
            .Select(user => new LehrerAuswahlItem(user.Id, AnzeigeName(user.FullName, user.Vorname, user.Nachname, user.UserName, user.Email)))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private Task<bool> IstAktiveDirektionAsync(Guid handelnderId, CancellationToken token)
        => db.Users.AsNoTracking()
            .AnyAsync(user => user.Id == handelnderId && !user.IsBlocked
                && db.UserRoles.Any(ur => ur.UserId == user.Id
                    && db.Roles.Any(role => role.Id == ur.RoleId
                        && (role.Name == Rollen.SchulLeiter || role.Name == Rollen.Koordinator))), token);

    private static string AnzeigeName(string? fullName, string? vorname, string? nachname, string? userName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName.Trim();
        }

        var name = $"{vorname} {nachname}".Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return userName ?? email ?? "Enseignant";
    }
}
