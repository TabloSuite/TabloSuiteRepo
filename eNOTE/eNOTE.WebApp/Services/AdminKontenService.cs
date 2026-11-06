using System.ComponentModel.DataAnnotations;
using ENote.Core.DataModels.Identity;
using ENote.WebApp.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

public class AdminKontenService(IDbContextFactory<ApplicationDbContext> factory, UserManager<AppUser> users)
{
    public record AdminKonto(Guid Id, string Vorname, string Nachname, string Email, bool Gesperrt, bool ErsterAdmin, string Pseudonym);

    internal static async Task PruefeAsync(ApplicationDbContext db, Guid id, bool erster, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id && !u.IsBlocked && (!erster || u.IstErsterAdmin)
            && db.UserRoles.Any(ur => ur.UserId == id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.SuperAdmin)), ct))
            throw new UnauthorizedAccessException("Vous ne disposez pas des droits nécessaires.");
    }

    public async Task<List<AdminKonto>> GetAsync(Guid bearbeiter, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeAsync(db, bearbeiter, false, ct);
        return await db.Users.AsNoTracking().Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.SuperAdmin)))
            .OrderByDescending(u => u.IstErsterAdmin).ThenBy(u => u.FullName)
            .Select(u => new AdminKonto(u.Id, u.Vorname, u.Nachname, u.Email ?? "", u.IsBlocked, u.IstErsterAdmin, u.UserName ?? "")).ToListAsync(ct);
    }

    private static void Validiere(string vorname, string nachname, string email)
    {
        if (string.IsNullOrWhiteSpace(vorname) || string.IsNullOrWhiteSpace(nachname)
            || !new EmailAddressAttribute().IsValid(email.Trim()))
            throw new ArgumentException("Veuillez saisir le prénom, le nom et une adresse e-mail valide.");
    }

    private static void PruefeErgebnis(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    public async Task LegeAnAsync(Guid bearbeiter, string vorname, string nachname, string email, string passwort, CancellationToken ct = default, string? benutzername = null)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeAsync(db, bearbeiter, false, ct);
        Validiere(vorname, nachname, email);
        var pseudonym = (benutzername ?? email).Trim();
        if (string.IsNullOrWhiteSpace(pseudonym)) throw new ArgumentException("Veuillez choisir un pseudonyme unique.");
        if (await users.FindByNameAsync(pseudonym) is not null)
            throw new ArgumentException("Ce pseudonyme est déjà utilisé.");
        var konto = new AppUser { UserName = pseudonym, Email = email.Trim(), Vorname = vorname.Trim(), Nachname = nachname.Trim(),
            FullName = $"{vorname.Trim()} {nachname.Trim()}", EmailConfirmed = true };
        PruefeErgebnis(await users.CreateAsync(konto, passwort));
        var rolle = await users.AddToRoleAsync(konto, AppRollen.SuperAdmin);
        if (!rolle.Succeeded)
        {
            await users.DeleteAsync(konto);
            PruefeErgebnis(rolle);
        }
    }

    private static async Task PruefeZielAsync(ApplicationDbContext db, Guid bearbeiter, Guid ziel, CancellationToken ct)
    {
        await PruefeAsync(db, bearbeiter, true, ct);
        if (!await db.Users.AnyAsync(u => u.Id == ziel && !u.IstErsterAdmin
            && db.UserRoles.Any(ur => ur.UserId == ziel && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.SuperAdmin)), ct))
            throw new InvalidOperationException("Seuls les autres comptes administrateurs peuvent être modifiés ou supprimés ici.");
    }

    public async Task SpeichereAsync(Guid bearbeiter, Guid ziel, string vorname, string nachname, string email, bool gesperrt, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeZielAsync(db, bearbeiter, ziel, ct);
        Validiere(vorname, nachname, email);
        var konto = await users.FindByIdAsync(ziel.ToString()) ?? throw new InvalidOperationException("Compte introuvable.");
        konto.Vorname = vorname.Trim(); konto.Nachname = nachname.Trim(); konto.FullName = $"{konto.Vorname} {konto.Nachname}";
        konto.Email = email.Trim(); konto.EmailConfirmed = false; konto.IsBlocked = gesperrt;
        konto.ActiveSessionId = null; konto.UpdatedAtUtc = DateTime.UtcNow;
        PruefeErgebnis(await users.UpdateAsync(konto));
    }

    public async Task LoescheAsync(Guid bearbeiter, Guid ziel, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeZielAsync(db, bearbeiter, ziel, ct);
        var konto = await users.FindByIdAsync(ziel.ToString()) ?? throw new InvalidOperationException("Compte introuvable.");
        PruefeErgebnis(await users.DeleteAsync(konto));
    }
}
