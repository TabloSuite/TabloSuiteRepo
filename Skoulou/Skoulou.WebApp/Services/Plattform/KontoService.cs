using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Profildaten für /profil (nur lesend außer Name, Telefon, Geschlecht).</summary>
public sealed record ProfilDaten(
    string Identifiant, string Vorname, string Nachname, string? Email, string? Telefon, Geschlecht Geschlecht,
    string Rolle, string? Schule, string? Klasse, string? ElternTelefon, string? ElternEmail, DateTime? LetzteAnmeldungUtc);

/// <summary>Bearbeitbare Profilfelder.</summary>
public sealed record ProfilEingabe(string Vorname, string Nachname, string? Telefon, Geschlecht Geschlecht);

/// <summary>Stand der Nutzungsbedingungen für ein Konto.</summary>
public sealed record AgbStand(bool MussAkzeptieren, string Version, string Rolle, bool MussPasswortAendern);

/// <summary>
/// Kontodienste für die Oberfläche (Blazor): Profil, Nutzungsbedingungen, Vorabprüfung des Passwortwechsels.
/// Nutzt nur die DbContext-Factory (kein UserManager im Circuit).
/// </summary>
public sealed class KontoService(
    IDbContextFactory<SkoulouDbContext> dbFactory,
    IPasswordHasher<Konto> hasher,
    AuditService audit,
    TimeProvider zeit)
{
    public async Task<ProfilDaten> LadeProfilAsync(Guid kontoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var k = await db.Users.AsNoTracking().Where(x => x.Id == kontoId)
            .Select(x => new { x.UserName, x.Vorname, x.Nachname, x.Email, x.Telefon, x.Geschlecht, x.Rolle, x.LetzteAnmeldungUtc, Schule = x.Schule != null ? x.Schule.Name : null })
            .FirstOrDefaultAsync(ct) ?? throw new KeinZugriffFehler("Votre compte est introuvable.");

        string? klasse = null, elternTel = null, elternMail = null;
        if (k.Rolle == Rollen.Schueler)
        {
            klasse = await (from e in db.Einschreibungen
                            join kl in db.Klassen on e.KlasseId equals kl.Id
                            where e.SchuelerId == kontoId && e.Status == EinschreibungsStatus.Active
                            select kl.Name).FirstOrDefaultAsync(ct);
            var profil = await db.SchuelerProfile.AsNoTracking().Where(p => p.KontoId == kontoId)
                .Select(p => new { p.ElternTelefon, p.ElternEmail }).FirstOrDefaultAsync(ct);
            elternTel = profil?.ElternTelefon;
            elternMail = profil?.ElternEmail;
        }
        return new ProfilDaten(k.UserName ?? string.Empty, k.Vorname, k.Nachname, k.Email, k.Telefon, k.Geschlecht,
            Rollen.Bezeichnung(k.Rolle), k.Schule, klasse, elternTel, elternMail, k.LetzteAnmeldungUtc);
    }

    /// <summary>Validiert die Profileingabe; Meldung oder null.</summary>
    public static string? PruefeProfil(ProfilEingabe e)
    {
        if (string.IsNullOrWhiteSpace(e.Vorname) || e.Vorname.Trim().Length > 100) return "Indiquez votre prénom (100 caractères au maximum).";
        if (string.IsNullOrWhiteSpace(e.Nachname) || e.Nachname.Trim().Length > 100) return "Indiquez votre nom (100 caractères au maximum).";
        if (e.Telefon is { Length: > 50 }) return "Le numéro de téléphone est trop long (50 caractères au maximum).";
        if (!string.IsNullOrWhiteSpace(e.Telefon) && e.Telefon.Any(c => !(char.IsDigit(c) || c is '+' or ' ' or '-' or '.' or '(' or ')')))
            return "Le numéro de téléphone ne peut contenir que des chiffres, espaces et le signe +.";
        return null;
    }

    public async Task SpeichereProfilAsync(Guid kontoId, ProfilEingabe eingabe, CancellationToken ct = default)
    {
        if (PruefeProfil(eingabe) is { } fehler) throw new FachFehler(fehler);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var konto = await db.Users.FirstOrDefaultAsync(k => k.Id == kontoId, ct) ?? throw new KeinZugriffFehler("Votre compte est introuvable.");
        konto.Vorname = eingabe.Vorname.Trim();
        konto.Nachname = eingabe.Nachname.Trim();
        konto.Telefon = string.IsNullOrWhiteSpace(eingabe.Telefon) ? null : eingabe.Telefon.Trim();
        konto.Geschlecht = eingabe.Geschlecht;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Muss das Konto die Bedingungen (neu) akzeptieren bzw. das Startpasswort ändern?</summary>
    public async Task<AgbStand> AgbStandAsync(Guid kontoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var version = await db.PlattformEinstellungen.AsNoTracking().Where(p => p.Id == PlattformEinstellung.EinzigeId).Select(p => p.AgbVersion).FirstOrDefaultAsync(ct) ?? string.Empty;
        var k = await db.Users.AsNoTracking().Where(x => x.Id == kontoId)
            .Select(x => new { x.AgbVersion, x.Rolle, x.MussPasswortAendern }).FirstOrDefaultAsync(ct);
        if (k is null) return new AgbStand(false, version, string.Empty, false);
        return new AgbStand(!string.IsNullOrEmpty(version) && k.AgbVersion != version, version, k.Rolle, k.MussPasswortAendern);
    }

    /// <summary>Speichert die Zustimmung zur aktuellen Version (mit Audit).</summary>
    public async Task AgbAkzeptierenAsync(Guid kontoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var version = await db.PlattformEinstellungen.Where(p => p.Id == PlattformEinstellung.EinzigeId).Select(p => p.AgbVersion).FirstOrDefaultAsync(ct) ?? string.Empty;
        var konto = await db.Users.FirstOrDefaultAsync(k => k.Id == kontoId, ct) ?? throw new KeinZugriffFehler("Votre compte est introuvable.");
        if (konto.AgbVersion == version) return;
        konto.AgbVersion = version;
        konto.AgbAkzeptiertAmUtc = zeit.GetUtcNow().UtcDateTime;
        audit.Erfasse(db, konto.Id, konto.SchuleId, AuditAktionen.AgbAkzeptiert, nameof(Konto), konto.Id, $"Conditions d'utilisation acceptées (version {version}).");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Vorabprüfung eines Passwortwechsels (Meldung am Feld), bevor das Formular gesendet wird.</summary>
    public async Task<string?> PruefePasswortWechselAsync(Guid kontoId, string? aktuell, string? neu, string? bestaetigung, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(aktuell)) return "Indiquez votre mot de passe actuel.";
        if (PasswortService.PruefeNeues(neu, bestaetigung) is { } fehler) return fehler;
        if (aktuell == neu) return "Choisissez un mot de passe différent de l'actuel.";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var konto = await db.Users.AsNoTracking().FirstOrDefaultAsync(k => k.Id == kontoId, ct);
        if (konto?.PasswordHash is null) return "Votre compte est introuvable.";
        return hasher.VerifyHashedPassword(konto, konto.PasswordHash, aktuell) == PasswordVerificationResult.Failed
            ? PasswortService.MeldungAktuellFalsch : null;
    }
}
