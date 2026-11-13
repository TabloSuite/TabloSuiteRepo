using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.WebApp.Services;

/// <summary>
/// « Contact »: Die Direktion (Directeur, Coordinateur) sieht den Responsable produit ihrer Schule
/// (Name, E-Mail, Telefon falls eingetragen). Ohne Responsable bleibt das Kontaktformular an SKOULOU.
/// </summary>
public sealed class ResponsableKontaktService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public sealed record Responsable(string Name, string? Email, string? Telefon)
    {
        /// <summary>WhatsApp-Link nur bei internationaler Nummer (+228 …), sonst null.</summary>
        public string? WhatsAppLink => Telefon is { } t && (t.TrimStart().StartsWith('+') || t.TrimStart().StartsWith("00"))
            && new string(t.Where(char.IsDigit).ToArray()).TrimStart('0') is { Length: >= 8 } ziffern
                ? "https://wa.me/" + ziffern : null;

        public string? TelefonLink => string.IsNullOrWhiteSpace(Telefon) ? null
            : "tel:" + new string(Telefon.Where(z => char.IsDigit(z) || z == '+').ToArray());
    }

    public async Task<Responsable?> GetFuerDirektionAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var istDirektion = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && !u.IsBlocked
            && u.UserRoles.Any(r => r.Role.Name == Rollen.SchulLeiter || r.Role.Name == Rollen.Koordinator), cancellationToken);
        if (!istDirektion) return null;

        var schulen = db.UserSchools.Where(link => link.AppUserId == userId).Select(link => link.SchoolId);
        return await db.UserSchools.AsNoTracking()
            .Where(link => schulen.Contains(link.SchoolId) && link.AppUserId != userId && !link.AppUser.IsBlocked
                && link.AppUser.UserRoles.Any(r => r.Role.Name == Rollen.ProduktOwner))
            .OrderBy(link => link.AppUser.FullName)
            .Select(link => new Responsable(
                string.IsNullOrWhiteSpace(link.AppUser.FullName) ? "Votre responsable" : link.AppUser.FullName,
                link.AppUser.Email,
                string.IsNullOrWhiteSpace(link.AppUser.TelefonNummer) ? null : link.AppUser.TelefonNummer.Trim()))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
