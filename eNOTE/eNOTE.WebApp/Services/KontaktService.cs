using ENote.Core.DataModels.Identity;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// « Aide &amp; contact »: Der Besitzer sieht seinen Responsable (Name, E-Mail, Telefon falls eingetragen).
/// Ohne Responsable schreibt er TabloSuite über das Kontaktformular (Mail an contact@tablosuite.com).
/// </summary>
public class KontaktService(IDbContextFactory<ApplicationDbContext> factory, Mails.MailBenachrichtigungen? mails = null,
    ILogger<KontaktService>? logger = null)
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

    // Schutz des öffentlichen Formulars: höchstens so viele Nachrichten pro Stunde insgesamt bzw. pro E-Mail-Adresse.
    public const int OeffentlichMaxProStunde = 20;
    public const int OeffentlichMaxProAdresseProStunde = 3;
    private static readonly object Sperre = new();
    private static readonly List<(DateTime Zeit, string Adresse)> Verlauf = [];

    /// <summary>
    /// Öffentliches Kontaktformular (ohne Anmeldung): Nachricht direkt in den eNOTE-Posteingang plus Hinweis-Mail
    /// an contact@tablosuite.com. <paramref name="falle"/> ist ein unsichtbares Feld – füllt ein Bot es aus,
    /// wird still verworfen. Liefert eine Fehlermeldung oder null bei Erfolg.
    /// </summary>
    public async Task<string?> SendeOeffentlichAsync(string? name, string? email, string? telefon, string? thema, string? nachricht,
        string? falle, CancellationToken ct = default, DateTime? jetztUtc = null)
    {
        if (!string.IsNullOrEmpty(falle)) return null;
        var n = (name ?? "").Trim();
        var e = (email ?? "").Trim();
        var t = (telefon ?? "").Trim();
        var betreff = (thema ?? "").Trim();
        var text = (nachricht ?? "").Trim();
        if (n.Length is < 2 or > 80) return "Veuillez saisir votre nom.";
        if (e.Length > 200 || !System.Net.Mail.MailAddress.TryCreate(e, out var adresse) || !e.Contains('.'))
            return "Veuillez saisir une adresse e-mail valide : nous vous répondons par e-mail.";
        if (t.Length > 30) return "Le numéro de téléphone est trop long.";
        if (!Themen.Contains(betreff)) return "Veuillez choisir le sujet de votre message.";
        if (text.Length < NachrichtMin) return $"Le message doit contenir au moins {NachrichtMin} caractères.";
        if (text.Length > NachrichtMax) return $"Le message est trop long ({NachrichtMax} caractères au plus).";

        var jetzt = jetztUtc ?? ENote.Core.Services.AppUhr.UtcNow;
        lock (Sperre)
        {
            Verlauf.RemoveAll(v => v.Zeit < jetzt.AddHours(-1));
            if (Verlauf.Count >= OeffentlichMaxProStunde
                || Verlauf.Count(v => string.Equals(v.Adresse, adresse.Address, StringComparison.OrdinalIgnoreCase)) >= OeffentlichMaxProAdresseProStunde)
                return "Trop de messages pour le moment. Réessayez dans une heure ou écrivez à contact@tablosuite.com.";
            Verlauf.Add((jetzt, adresse.Address));
        }

        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            db.EingangsMails.Add(new ENote.Core.DataModels.Infrastruktur.EingangsMail
            {
                Absender = new System.Net.Mail.MailAddress(adresse.Address, n).ToString(),
                Empfaenger = "contact@tablosuite.com",
                Betreff = "[eNOTE] Contact : " + betreff,
                Text = text + "\n\n— Visiteur sans compte" + (t.Length > 0 ? " · Téléphone : " + t : "")
            });
            await db.SaveChangesAsync(ct);
        }

        var ok = mails is not null && await mails.KontaktAnfrageAsync(betreff, text, n, "Visiteur (sans compte)", adresse.Address,
            t.Length > 0 ? t : null, null, ct);
        if (!ok)
            logger?.LogWarning("Hinweis-Mail zur öffentlichen Kontaktnachricht nicht versendet (Nachricht liegt im Posteingang).");
        return null;
    }

    /// <summary>Nur für Tests: Zähler des öffentlichen Formulars zurücksetzen.</summary>
    internal static void SetzeZaehlerZurueck() { lock (Sperre) Verlauf.Clear(); }

    /// <summary>Nachricht an TabloSuite (contact@tablosuite.com). Liefert eine Fehlermeldung oder null bei Erfolg.</summary>
    public async Task<string?> SendeAnTabloSuiteAsync(Guid userId, Guid? salonId, string? thema, string? nachricht, CancellationToken ct = default)
    {
        var betreff = (thema ?? "").Trim();
        var text = (nachricht ?? "").Trim();
        if (betreff.Length == 0) return "Veuillez choisir le sujet de votre message.";
        if (betreff.Length > BetreffMax) return "Le sujet est trop long.";
        if (text.Length < NachrichtMin) return $"Le message doit contenir au moins {NachrichtMin} caractères.";
        if (text.Length > NachrichtMax) return $"Le message est trop long ({NachrichtMax} caractères au plus).";

        await using var db = await factory.CreateDbContextAsync(ct);
        var absender = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.FullName, u.UserName, u.Email, u.TelefonNummer }).FirstOrDefaultAsync(ct);
        if (absender is null) return "Compte introuvable.";
        var laden = salonId is Guid id
            ? await db.Salons.AsNoTracking().Where(s => s.Id == id).Select(s => s.Name).FirstOrDefaultAsync(ct)
            : null;

        // Direkt in den eNOTE-Posteingang (mit echter Absenderadresse, damit « Répondre » beim Kunden ankommt) …
        var name = absender.FullName ?? absender.UserName ?? "";
        db.EingangsMails.Add(new ENote.Core.DataModels.Infrastruktur.EingangsMail
        {
            Absender = System.Net.Mail.MailAddress.TryCreate(absender.Email?.Trim(), name, out var adresse) ? adresse.ToString() : name,
            Empfaenger = "contact@tablosuite.com",
            Betreff = "[eNOTE] Contact : " + betreff,
            Text = text + "\n\n— Compte : " + (absender.UserName ?? "") + (laden is null ? "" : " · Établissement : " + laden)
                + (string.IsNullOrWhiteSpace(absender.TelefonNummer) ? "" : " · Téléphone : " + absender.TelefonNummer)
        });
        await db.SaveChangesAsync(ct);

        // … und als Hinweis-Mail an contact@tablosuite.com (kommt nicht erneut in den Posteingang).
        var ok = mails is not null && await mails.KontaktAnfrageAsync(betreff, text, name, absender.UserName ?? "",
            absender.Email, absender.TelefonNummer, laden, ct);
        if (!ok)
            logger?.LogWarning("Hinweis-Mail zur Kontaktnachricht nicht versendet (Nachricht liegt im Posteingang).");
        return null;
    }
}
