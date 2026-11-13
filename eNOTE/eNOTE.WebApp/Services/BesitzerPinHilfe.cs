using ENote.Core.DataModels.Identity;
using ENote.WebApp.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Besitzer-PIN vergessen: Der angemeldete Besitzer bestätigt sich mit dem Passwort seines Kontos
/// (Fehlversuche zählen zur Kontosperre). Die PIN selbst wird nie per Mail verschickt.
/// </summary>
public class BesitzerPinHilfe(UserManager<AppUser> users, IDbContextFactory<ApplicationDbContext> factory)
{
    public async Task<(bool Erfolg, string? Fehler)> PruefePasswortAsync(
        Guid userId, Guid salonId, string passwort, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(passwort))
            return (false, "Veuillez saisir votre mot de passe.");

        var konto = await users.FindByIdAsync(userId.ToString());
        if (konto is null || konto.IsBlocked || !await users.IsInRoleAsync(konto, AppRollen.Besitzer))
            return (false, "Seul le propriétaire peut débloquer avec son mot de passe. Demandez-lui de le faire.");

        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            if (!await db.UserSalons.AnyAsync(us => us.AppUserId == userId && us.SalonId == salonId, ct))
                return (false, "Seul le propriétaire peut débloquer avec son mot de passe. Demandez-lui de le faire.");
        }

        if (await users.IsLockedOutAsync(konto))
            return (false, "Trop d'essais. Réessayez dans quelques minutes.");

        if (!await users.CheckPasswordAsync(konto, passwort))
        {
            await users.AccessFailedAsync(konto);
            return (false, "Mot de passe incorrect.");
        }

        await users.ResetAccessFailedCountAsync(konto);
        return (true, null);
    }
}
