using Microsoft.AspNetCore.Identity;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.Models.Import;
using SkoulouApp.Core.Services;

namespace SkoulouApp.WebApp.Services;

/// <summary>
/// Neues Startpasswort durch die Direktion (Schüler, Lehrkräfte, Koordinatoren – nur Konten, die sie verwalten darf).
/// Gedacht für Konten ohne E-Mail, die « Mot de passe oublié » nicht nutzen können. Laufende Sitzungen werden beendet.
/// </summary>
public sealed class KontoZugangService(DashboardService dashboard, UserManager<AppUser> userManager)
{
    public async Task<SchoolUserImportZugang> NeuesPasswortAsync(Guid direktorId, Guid benutzerId, CancellationToken token = default)
    {
        if (direktorId == benutzerId)
            throw new InvalidOperationException("Changez votre propre mot de passe depuis votre profil.");
        if (await dashboard.GetSchoolUserForEditAsync(direktorId, benutzerId, token) is null)
            throw new InvalidOperationException("Ce compte n'appartient pas à votre établissement ou ne peut pas être géré ici.");

        var benutzer = await userManager.FindByIdAsync(benutzerId.ToString()) ?? throw new InvalidOperationException("Compte introuvable.");
        var passwort = ErstzugangService.PasswortErzeugen();
        var jeton = await userManager.GeneratePasswordResetTokenAsync(benutzer);
        var ergebnis = await userManager.ResetPasswordAsync(benutzer, jeton, passwort);
        if (!ergebnis.Succeeded)
            throw new InvalidOperationException(string.Join(" ", ergebnis.Errors.Select(e => e.Description)));

        benutzer.ActiveSessionId = Guid.NewGuid(); // laufende Sitzungen beenden
        await userManager.UpdateAsync(benutzer);
        return new SchoolUserImportZugang(benutzer.UserName ?? string.Empty, passwort);
    }
}
