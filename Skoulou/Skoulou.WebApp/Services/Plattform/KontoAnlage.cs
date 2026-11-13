using Microsoft.AspNetCore.Identity;
using Skoulou.Core.Entitaeten;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Legt ein Konto mit genau einer Rolle an (Konto.Rolle und Identity-Rolle gemeinsam).</summary>
public static class KontoAnlage
{
    public static async Task<IdentityResult> AnlegenAsync(UserManager<Konto> users, Konto konto, string rolle, string? passwort)
    {
        konto.Rolle = rolle;
        konto.Email = string.IsNullOrWhiteSpace(konto.Email) ? null : konto.Email.Trim();
        konto.EmailConfirmed = konto.Email is not null;
        var ergebnis = passwort is null ? await users.CreateAsync(konto) : await users.CreateAsync(konto, passwort);
        return ergebnis.Succeeded ? await users.AddToRoleAsync(konto, rolle) : ergebnis;
    }

    /// <summary>Fehlertexte ohne sensible Werte (für Logs und Meldungen).</summary>
    public static string Fehlertext(IdentityResult ergebnis) => string.Join(" ", ergebnis.Errors.Select(e => e.Code));
}
