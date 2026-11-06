using System.Text.RegularExpressions;
using ENote.Core.DataModels.Identity;
using Microsoft.AspNetCore.Identity;

namespace ENote.WebApp.Services;

public class KontoProfilService(UserManager<AppUser> users)
{
    public record KontoProfil(string Name, string Pseudonym, string Email, IList<string> Rollen);

    public async Task<KontoProfil?> GetAsync(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null || user.IsBlocked || user.IstInternerKassierer || await users.IsInRoleAsync(user, AppRollen.Kassierer)) return null;
        return new(user.FullName, user.UserName ?? "", user.Email ?? "", await users.GetRolesAsync(user));
    }

    public async Task<(bool Erfolg, string? Fehler)> AenderePseudonymAsync(Guid id, string pseudonym, string passwort)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null || user.IsBlocked || user.IstInternerKassierer || await users.IsInRoleAsync(user, AppRollen.Kassierer))
            return (false, "Ce compte ne peut pas être modifié.");
        if (!await users.CheckPasswordAsync(user, passwort)) return (false, "Le mot de passe actuel est incorrect.");
        var name = pseudonym.Trim();
        if (!Regex.IsMatch(name, @"\A[a-zA-Z0-9._-]{3,64}\z"))
            return (false, "Choisissez 3 à 64 caractères : lettres, chiffres, point, tiret ou soulignement.");
        var vorhanden = await users.FindByNameAsync(name);
        if (vorhanden is not null && vorhanden.Id != id) return (false, "Ce pseudonyme est déjà utilisé.");
        if (user.UserName == name) return (false, "Choisissez un pseudonyme différent.");
        user.UserName = name;
        user.ActiveSessionId = null;
        user.SecurityStamp = Guid.NewGuid().ToString();
        user.UpdatedAtUtc = DateTime.UtcNow;
        var result = await users.UpdateAsync(user);
        return result.Succeeded ? (true, null) : (false, "Le pseudonyme n'a pas pu être enregistré. Il est peut-être déjà utilisé.");
    }
}
