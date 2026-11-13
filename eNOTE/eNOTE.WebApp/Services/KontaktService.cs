using ENote.Core.DataModels.Identity;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// « Aide &amp; contact »: Der Besitzer sieht seinen Responsable (Name, E-Mail, Telefon falls eingetragen).
/// Ohne Responsable schreibt er TabloSuite über das Kontaktformular (Mail an contact@tablosuite.com).
/// </summary>
public class KontaktService(IDbContextFactory<ApplicationDbContext> factory, Mails.MailBenachrichtigungen? mails = null)
{
    public const int BetreffMax = 120;
    public const int NachrichtMin = 10;
    public const int NachrichtMax = 2000;

    public static readonly IReadOnlyList<string> Themen =
        ["Question sur eNOTE", "Problème technique", "Abonnement et paiement", "Autre demande"];

    public sealed record Responsable(string Name, string? Email, string? Telefon)
    {
        /// <summary>WhatsApp-Link nur bei internationaler Nummer (+228 …), sonst null.</summary>
        public string? WhatsAppLink => Telefon is { } t && (t.TrimStart().StartsWith('+') || t.TrimStart().StartsWith("00"))
            && new string(t.Where(char.IsDigit).ToArray()).TrimStart('0') is { Length: >= 8 } ziffern
                ? "https://wa.me/" + ziffern : null;

        public string? TelefonLink => string.IsNullOrWhiteSpace(Telefon) ? null
            : "tel:" + new string(Telefon.Where(z => char.IsDigit(z) || z == '+').ToArray());
    }

    /// <summary>
    /// Responsable des Besitzers: zuerst die persönliche Betreuung, sonst die aktive Zuweisung des Ladens.
    /// Nur für den Besitzer selbst (Mitarbeiter sehen keine Kontaktdaten des Responsables).
    /// </summary>
    public async Task<Responsable?> GetResponsableAsync(Guid besitzerId, Guid salonId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var istBesitzer = await db.UserSalons.AnyAsync(us => us.AppUserId == besitzerId && us.SalonId == salonId
            && db.UserRoles.Any(ur => ur.UserId == besitzerId && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.Besitzer)), ct);
        if (!istBesitzer) return null;

        var poId = await db.BesitzerBetreuungen.AsNoTracking()
            .Where(b => b.BesitzerId == besitzerId && b.GueltigBisUtc == null)
            .Select(b => (Guid?)b.ProduktOwnerId)
            .FirstOrDefaultAsync(ct)
            ?? await db.ProduktOwnerSalons.AsNoTracking()
                .Where(p => p.SalonId == salonId && p.GueltigBisUtc == null)
                .OrderByDescending(p => p.CreatedAtUtc)
                .Select(p => (Guid?)p.ProduktOwnerId)
                .FirstOrDefaultAsync(ct);
        if (poId is null) return null;

        return await db.Users.AsNoTracking()
            .Where(u => u.Id == poId && !u.IsBlocked
                && db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.ProduktOwner)))
            .Select(u => new Responsable(
                string.IsNullOrWhiteSpace(u.FullName) ? "Votre responsable" : u.FullName,
                u.Email,
                string.IsNullOrWhiteSpace(u.TelefonNummer) ? null : u.TelefonNummer.Trim()))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Nachricht an TabloSuite (contact@tablosuite.com). Liefert eine Fehlermeldung oder null bei Erfolg.</summary>
    public async Task<string?> SendeAnTabloSuiteAsync(Guid userId, Guid? salonId, string? thema, string? nachricht, CancellationToken ct = default)
    {
        var betreff = (thema ?? "").Trim();
        var text = (nachricht ?? "").Trim();
        if (betreff.Length == 0) return "Veuillez choisir le sujet de votre message.";
        if (betreff.Length > BetreffMax) return "Le sujet est trop long.";
        if (text.Length < NachrichtMin) return $"Le message doit contenir au moins {NachrichtMin} caractères.";
        if (text.Length > NachrichtMax) return $"Le message est trop long ({NachrichtMax} caractères au plus).";
        if (mails is null) return "L'envoi d'e-mails n'est pas disponible. Écrivez-nous à contact@tablosuite.com.";

        await using var db = await factory.CreateDbContextAsync(ct);
        var absender = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.FullName, u.UserName, u.Email, u.TelefonNummer }).FirstOrDefaultAsync(ct);
        if (absender is null) return "Compte introuvable.";
        var laden = salonId is Guid id
            ? await db.Salons.AsNoTracking().Where(s => s.Id == id).Select(s => s.Name).FirstOrDefaultAsync(ct)
            : null;

        var ok = await mails.KontaktAnfrageAsync(betreff, text, absender.FullName ?? absender.UserName ?? "", absender.UserName ?? "",
            absender.Email, absender.TelefonNummer, laden, ct);
        return ok ? null : "L'envoi a échoué. Réessayez plus tard ou écrivez-nous à contact@tablosuite.com.";
    }
}
