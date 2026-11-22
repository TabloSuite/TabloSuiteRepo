using System.Security.Claims;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Infrastruktur;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services.Storage;

/// <summary>Autorisiert anhand aktueller DB-Zuordnungen, nicht anhand eines Blob-Prefix oder E-Mail.</summary>
public class MedienZugriff(IDbContextFactory<ApplicationDbContext> factory)
{
    /// <summary>Erstellt einen DbContext für direkte Abfragen (z. B. in MedienEndpunkte).</summary>
    public async Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken ct = default)
        => await factory.CreateDbContextAsync(ct);

    public async Task<BlobMediaAsset?> LadeAssetAsync(ClaimsPrincipal principal, string blobName, CancellationToken ct = default)
    {
        if (!MedienDateien.IstGueltigerName(blobName)) return null;
        await using var db = await factory.CreateDbContextAsync(ct);
        var asset = await db.BlobMediaAssets.AsNoTracking().FirstOrDefaultAsync(a => a.BlobName == blobName, ct);
        if (asset is null) return null;
        var istBeleg = await db.Ausgaben.AnyAsync(a => a.BelegAssetId == asset.Id, ct);
        return await HatZugangAsync(db, principal, asset.SalonId, istBeleg, ct) ? asset : null;
    }

    public async Task<bool> HatSalonZugangAsync(ClaimsPrincipal principal, Guid salonId, bool finanzen = false, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await HatZugangAsync(db, principal, salonId, finanzen, ct);
    }

    /// <summary>
    /// Kundendaten (Name auf dem Bon) nur für Mitglieder des Ladens (Besitzer, Employés). Responsable/Admin dürfen den Bon
    /// eines Ladens sehen, aber nie den Kunden (Fehlersuche 21.11.2026: PO sah « Client : … » in /bon/{id}/druck).
    /// </summary>
    public async Task<bool> DarfKundenSehenAsync(ClaimsPrincipal principal, Guid salonId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return false;
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.UserSalons.AnyAsync(us => us.AppUserId == userId && us.SalonId == salonId && !us.AppUser.IsBlocked, ct);
    }

    private static async Task<bool> HatZugangAsync(ApplicationDbContext db, ClaimsPrincipal principal,
        Guid? salonId, bool finanzen, CancellationToken ct)
    {
        if (principal.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !await db.Users.AnyAsync(u => u.Id == userId && !u.IsBlocked, ct)) return false;

        var rollen = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                            where ur.UserId == userId select r.Name).ToListAsync(ct);
        if (salonId is null) return rollen.Contains(AppRollen.SuperAdmin);
        if (!await db.Salons.AnyAsync(s => s.Id == salonId && s.IstAktiv, ct)) return false;
        if (rollen.Contains(AppRollen.SuperAdmin)) return true;
        if (rollen.Contains(AppRollen.ProduktOwner)
            && await db.ProduktOwnerSalons.AnyAsync(p => p.ProduktOwnerId == userId && p.SalonId == salonId
                && p.GueltigBisUtc == null && (!finanzen || p.KannLadenUmsatzSehen), ct)) return true;
        return await db.UserSalons.AnyAsync(us => us.AppUserId == userId && us.SalonId == salonId
            && (!finanzen || us.KannFinanzenSehen || rollen.Contains(AppRollen.Besitzer)), ct);
    }
}
