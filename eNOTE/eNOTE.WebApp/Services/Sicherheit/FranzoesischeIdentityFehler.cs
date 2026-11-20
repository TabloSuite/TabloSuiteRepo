using Microsoft.AspNetCore.Identity;

namespace ENote.WebApp.Services.Sicherheit;

/// <summary>
/// Französische Meldungen von ASP.NET Identity (Klicktest 20.11.2026: bei der Registrierung erschien z. B.
/// « Passwords must have at least one digit ('0'-'9'). »). Gilt überall, wo Identity-Fehler angezeigt werden:
/// Inscription, Mot de passe oublié, Mon profil, Équipe (Connexion personnelle), Administration.
/// </summary>
public sealed class FranzoesischeIdentityFehler : IdentityErrorDescriber
{
    private static IdentityError F(string code, string text) => new() { Code = code, Description = text };

    public override IdentityError DefaultError() => F(nameof(DefaultError), "Une erreur s'est produite. Réessayez.");
    public override IdentityError ConcurrencyFailure() => F(nameof(ConcurrencyFailure), "Le compte a été modifié entre-temps. Rechargez la page et réessayez.");
    public override IdentityError PasswordMismatch() => F(nameof(PasswordMismatch), "Mot de passe incorrect.");
    public override IdentityError InvalidToken() => F(nameof(InvalidToken), "Ce lien n'est plus valable. Demandez-en un nouveau.");
    public override IdentityError RecoveryCodeRedemptionFailed() => F(nameof(RecoveryCodeRedemptionFailed), "Code de récupération invalide.");
    public override IdentityError LoginAlreadyAssociated() => F(nameof(LoginAlreadyAssociated), "Cette connexion est déjà liée à un autre compte.");
    public override IdentityError InvalidUserName(string? userName)
        => F(nameof(InvalidUserName), "Le pseudonyme ne peut contenir que des lettres sans accent, des chiffres et les signes . - _ @ + (sans espace).");
    public override IdentityError InvalidEmail(string? email) => F(nameof(InvalidEmail), "Adresse e-mail invalide.");
    public override IdentityError DuplicateUserName(string userName) => F(nameof(DuplicateUserName), "Ce pseudonyme est déjà utilisé.");
    public override IdentityError DuplicateEmail(string email) => F(nameof(DuplicateEmail), "Cette adresse e-mail est déjà utilisée.");
    public override IdentityError InvalidRoleName(string? role) => F(nameof(InvalidRoleName), "Rôle invalide.");
    public override IdentityError DuplicateRoleName(string role) => F(nameof(DuplicateRoleName), "Ce rôle existe déjà.");
    public override IdentityError UserAlreadyHasPassword() => F(nameof(UserAlreadyHasPassword), "Ce compte a déjà un mot de passe.");
    public override IdentityError UserLockoutNotEnabled() => F(nameof(UserLockoutNotEnabled), "Le blocage n'est pas activé pour ce compte.");
    public override IdentityError UserAlreadyInRole(string role) => F(nameof(UserAlreadyInRole), "Le compte a déjà ce rôle.");
    public override IdentityError UserNotInRole(string role) => F(nameof(UserNotInRole), "Le compte n'a pas ce rôle.");
    public override IdentityError PasswordTooShort(int length) => F(nameof(PasswordTooShort), $"Le mot de passe doit contenir au moins {length} caractères.");
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars)
        => F(nameof(PasswordRequiresUniqueChars), $"Le mot de passe doit contenir au moins {uniqueChars} caractères différents.");
    public override IdentityError PasswordRequiresNonAlphanumeric()
        => F(nameof(PasswordRequiresNonAlphanumeric), "Le mot de passe doit contenir au moins un caractère spécial (ex. ! ou #).");
    public override IdentityError PasswordRequiresDigit() => F(nameof(PasswordRequiresDigit), "Le mot de passe doit contenir au moins un chiffre (0-9).");
    public override IdentityError PasswordRequiresLower() => F(nameof(PasswordRequiresLower), "Le mot de passe doit contenir au moins une lettre minuscule (a-z).");
    public override IdentityError PasswordRequiresUpper() => F(nameof(PasswordRequiresUpper), "Le mot de passe doit contenir au moins une lettre majuscule (A-Z).");
}
