using System.Security.Claims;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Authentifizierung und öffentliche Registrierung (Besitzer legt sich mit
/// seinem Salon an; der SuperAdmin gibt den Salon anschließend frei).
/// </summary>
public class AuthService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    ILogger<AuthService> logger)
{
    public async Task<(bool Succeeded, string? Error)> LoginAsync(
        string email,
        string passwort,
        bool dauerhaft,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user is null || user.IsBlocked)
        {
            return (false, "invalid-credentials");
        }

        var ergebnis = await signInManager.CheckPasswordSignInAsync(user, passwort, lockoutOnFailure: true);
        if (!ergebnis.Succeeded)
        {
            return (false, "invalid-credentials");
        }

        // Eine neue Anmeldung meldet eine frühere Sitzung auf einem anderen Gerät ab.
        user.ActiveSessionId = Guid.NewGuid();
        user.UpdatedAtUtc = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        await signInManager.SignInAsync(user, dauerhaft);

        // Session-Claim setzen, damit OnValidatePrincipal die aktive Sitzung prüfen kann.
        if (signInManager.Context.User.Identity is ClaimsIdentity)
        {
            await signInManager.SignOutAsync();
            var principal = await signInManager.CreateUserPrincipalAsync(user);
            var identity = (ClaimsIdentity)principal.Identity!;
            identity.AddClaim(new Claim("enote_session", user.ActiveSessionId.Value.ToString()));
            await signInManager.Context.SignInAsync(
                IdentityConstants.ApplicationScheme,
                principal,
                new Microsoft.AspNetCore.Authentication.AuthenticationProperties { IsPersistent = dauerhaft });
        }

        return (true, null);
    }

    public async Task LogoutAsync() => await signInManager.SignOutAsync();

    /// <summary>Ändert das Passwort des angemeldeten Nutzers (aktuelles Passwort erforderlich).</summary>
    public async Task<(bool Erfolg, string? Fehler)> AenderePasswortAsync(
        Guid userId, string aktuellesPasswort, string neuesPasswort, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return (false, "Utilisateur introuvable.");
        }

        var ergebnis = await userManager.ChangePasswordAsync(user, aktuellesPasswort, neuesPasswort);
        if (!ergebnis.Succeeded)
        {
            var fehler = string.Join(" ", ergebnis.Errors.Select(e => e.Description));
            return (false, string.IsNullOrWhiteSpace(fehler) ? "Le changement de mot de passe a échoué." : fehler);
        }

        // Sitzung rotieren, damit andere Geräte abgemeldet werden.
        user.ActiveSessionId = Guid.NewGuid();
        user.UpdatedAtUtc = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
        return (true, null);
    }

    public async Task<(bool Succeeded, string ErrorMessage)> RegisterBesitzerAsync(
        string vorname,
        string nachname,
        string email,
        string passwort,
        string salonName,
        string? salonStandort,
        string? salonSlogan = null,
        CancellationToken cancellationToken = default)
    {
        var bereinigtEmail = email.Trim();
        var bereinigtSalon = salonName.Trim();

        if (await userManager.FindByEmailAsync(bereinigtEmail) is not null)
        {
            return (false, "Un compte avec cette adresse e-mail existe déjà.");
        }

        if (string.IsNullOrWhiteSpace(bereinigtSalon))
        {
            return (false, "Veuillez indiquer le nom de votre salon.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Salons.AnyAsync(s => s.Name == bereinigtSalon, cancellationToken))
        {
            return (false, "Un salon portant ce nom existe déjà.");
        }

        var besitzer = new AppUser
        {
            UserName = bereinigtEmail,
            Email = bereinigtEmail,
            Vorname = vorname.Trim(),
            Nachname = nachname.Trim(),
            FullName = $"{vorname.Trim()} {nachname.Trim()}",
            EmailConfirmed = true,
            IsSelfRegistered = true,
            // Konto ist nutzbar, aber der Salon bleibt bis zur Freigabe gesperrt.
            IsBlocked = false
        };

        var erstellen = await userManager.CreateAsync(besitzer, passwort);
        if (!erstellen.Succeeded)
        {
            var fehler = string.Join(" ", erstellen.Errors.Select(e => e.Description));
            logger.LogWarning("Besitzer-Registrierung fehlgeschlagen für {Email}: {Fehler}", bereinigtEmail, fehler);
            return (false, string.IsNullOrWhiteSpace(fehler) ? "L'inscription a échoué." : fehler);
        }

        await userManager.AddToRoleAsync(besitzer, AppRollen.Besitzer);

        var salon = new Salon
        {
            Name = bereinigtSalon,
            Standort = string.IsNullOrWhiteSpace(salonStandort) ? null : salonStandort.Trim(),
            Slogan = string.IsNullOrWhiteSpace(salonSlogan) ? null : salonSlogan.Trim(),
            IstAktiv = true,
            IstFreigegeben = false
        };

        db.Salons.Add(salon);
        await db.SaveChangesAsync(cancellationToken);

        db.UserSalons.Add(new UserSalon
        {
            AppUserId = besitzer.Id,
            SalonId = salon.Id,
            // Besitzer hat alle salonbezogenen Rechte.
            KannFinanzenSehen = true,
            KannLeistungenVerwalten = true,
            KannRabatteVerwalten = true
        });
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Neuer Salon registriert (freizugeben): {Salon} durch {Email}", bereinigtSalon, bereinigtEmail);
        return (true, string.Empty);
    }
}
