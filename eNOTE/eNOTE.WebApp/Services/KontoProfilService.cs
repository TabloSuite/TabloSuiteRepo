using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using ENote.Core.DataModels.Identity;
using ENote.WebApp.Services.Mails;
using Microsoft.AspNetCore.Identity;

namespace ENote.WebApp.Services;

/// <summary>
/// Eigene Kontodaten für Administrateur, Responsable (PO) und Propriétaire: Name, Telefon, E-Mail, Identifiant.
/// Kassierer-Konten werden vom Besitzer verwaltet und sind hier ausgeschlossen.
/// </summary>
public class KontoProfilService(UserManager<AppUser> users, MailBenachrichtigungen? mails = null)
{
    public const int MaxNamenLaenge = 60;
    public const int MaxTelefonLaenge = 30;

    /// <summary>
    /// Prénom/Nom beim Anlegen eines Kontos (Mitarbeiter, Besitzer durch Admin/Responsable, zweiter Laden): Pflicht und höchstens
    /// <see cref="MaxNamenLaenge"/> Zeichen. Klicktest 20.11.2026: eine Mitarbeiterin mit 270 Zeichen Namen ließ sich anlegen,
    /// jeder Verkauf scheiterte danach am Kassierernamen des Bons (Spalte 200 Zeichen) – « Un problème est survenu ».
    /// Null = in Ordnung, sonst die französische Meldung.
    /// </summary>
    public static string? PruefeNamen(string? vorname, string? nachname, bool pflicht = true)
    {
        var v = (vorname ?? string.Empty).Trim();
        var n = (nachname ?? string.Empty).Trim();
        if (pflicht && (v.Length == 0 || n.Length == 0)) return "Veuillez saisir le prénom et le nom.";
        if (v.Length > MaxNamenLaenge || n.Length > MaxNamenLaenge) return $"Le prénom et le nom sont limités à {MaxNamenLaenge} caractères.";
        return null;
    }

    public record KontoProfil(string Vorname, string Nachname, string Name, string Pseudonym, string Email, string? Telefon, IList<string> Rollen);

    public async Task<KontoProfil?> GetAsync(Guid id)
    {
        var user = await BearbeitbarAsync(id);
        if (user is null) return null;
        return new(user.Vorname, user.Nachname, user.FullName, user.UserName ?? "", user.Email ?? "", user.TelefonNummer, await users.GetRolesAsync(user));
    }

    /// <summary>Prénom, nom et téléphone (facultatif). Keine Abmeldung nötig.</summary>
    public async Task<(bool Erfolg, string? Fehler)> AenderePersoenlicheDatenAsync(Guid id, string vorname, string nachname, string? telefon)
    {
        var user = await BearbeitbarAsync(id);
        if (user is null) return (false, "Ce compte ne peut pas être modifié.");

        var v = NormalisiereName(vorname);
        var n = NormalisiereName(nachname);
        if (v.Length == 0 || n.Length == 0) return (false, "Veuillez saisir le prénom et le nom.");
        if (v.Length > MaxNamenLaenge || n.Length > MaxNamenLaenge) return (false, $"Le prénom et le nom sont limités à {MaxNamenLaenge} caractères.");

        var tel = string.IsNullOrWhiteSpace(telefon) ? null : Regex.Replace(telefon.Trim(), @"\s+", " ");
        if (tel is not null && (tel.Length > MaxTelefonLaenge || !Regex.IsMatch(tel, @"\A\+?[0-9 ()./-]{6,}\z") || tel.Count(char.IsDigit) < 6))
            return (false, "Numéro de téléphone invalide (chiffres, espaces et + uniquement).");

        user.Vorname = v;
        user.Nachname = n;
        user.FullName = $"{v} {n}";
        user.TelefonNummer = tel;
        user.UpdatedAtUtc = DateTime.UtcNow;
        var result = await users.UpdateAsync(user);
        return result.Succeeded ? (true, null) : (false, "Les informations n'ont pas pu être enregistrées.");
    }

    /// <summary>
    /// Neue E-Mail-Adresse (Kontakt, Rechnungen, « Mot de passe oublié »). Nur mit aktuellem Passwort;
    /// die bisherige Adresse erhält einen Sicherheitshinweis.
    /// </summary>
    public async Task<(bool Erfolg, string? Fehler)> AendereEmailAsync(Guid id, string email, string passwort)
    {
        var user = await BearbeitbarAsync(id);
        if (user is null) return (false, "Ce compte ne peut pas être modifié.");
        if (!await users.CheckPasswordAsync(user, passwort ?? "")) return (false, "Le mot de passe actuel est incorrect.");

        var neu = (email ?? "").Trim();
        if (neu.Length > 256 || !new EmailAddressAttribute().IsValid(neu) || !neu.Contains('.'))
            return (false, "Veuillez saisir une adresse e-mail valide.");
        var alt = user.Email ?? "";
        if (string.Equals(alt, neu, StringComparison.OrdinalIgnoreCase)) return (false, "C'est déjà votre adresse actuelle.");

        // Eindeutigkeit prüft der Identity-Validator gemäß Konfiguration (RequireUniqueEmail).
        user.Email = neu;
        user.EmailConfirmed = false;
        user.UpdatedAtUtc = DateTime.UtcNow;
        IdentityResult result;
        try
        {
            result = await users.UpdateAsync(user);
        }
        catch (InvalidOperationException)
        {
            return (false, "Cette adresse e-mail est déjà utilisée par un autre compte.");
        }

        if (!result.Succeeded)
        {
            return (false, result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateEmail))
                ? "Cette adresse e-mail est déjà utilisée par un autre compte."
                : "L'adresse n'a pas pu être enregistrée.");
        }

        if (mails is not null && alt.Length > 0)
            await mails.EmailGeaendertAsync(user.Id, alt, neu);
        return (true, null);
    }

    public async Task<(bool Erfolg, string? Fehler)> AenderePseudonymAsync(Guid id, string pseudonym, string passwort)
    {
        var user = await BearbeitbarAsync(id);
        if (user is null) return (false, "Ce compte ne peut pas être modifié.");
        if (!await users.CheckPasswordAsync(user, passwort ?? "")) return (false, "Le mot de passe actuel est incorrect.");
        var name = (pseudonym ?? "").Trim();
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

    /// <summary>Nur aktive Konten von Admin, PO und Besitzer (keine Kassierer).</summary>
    private async Task<AppUser?> BearbeitbarAsync(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null || user.IsBlocked || user.IstInternerKassierer || await users.IsInRoleAsync(user, AppRollen.Kassierer)) return null;
        return user;
    }

    private static string NormalisiereName(string? wert) => Regex.Replace((wert ?? "").Trim(), @"\s+", " ");
}
