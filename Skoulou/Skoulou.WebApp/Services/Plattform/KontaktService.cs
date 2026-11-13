using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Kontaktdaten des Responsable einer Schule.</summary>
public sealed record ResponsableKontakt(string Name, string? Email, string? Telefon, string? WhatsApp);

/// <summary>Eingabe des Kontaktformulars.</summary>
public sealed record KontaktEingabe(string? Name, string? Email, string? Sujet, string? Nachricht);

/// <summary>
/// Kontaktformular: speichert die Nachricht in der Messagerie (Eingang) und reiht eine Mail an den
/// Support ein (Antwort an den Absender). Speichern und Versand sind entkoppelt (Outbox).
/// </summary>
public sealed class KontaktService(
    IDbContextFactory<SkoulouDbContext> dbFactory,
    MailAusgangService mails,
    IOptionsMonitor<PostmarkOptionen> postmark)
{
    public const int NachrichtMin = 10;
    public const int NachrichtMax = 5000;

    /// <summary>Validierung; Meldung oder null.</summary>
    public static string? Pruefe(KontaktEingabe e)
    {
        if (string.IsNullOrWhiteSpace(e.Name) || e.Name.Trim().Length > 100) return "Indiquez votre nom.";
        if (!MailAusgangService.IstAdresse(e.Email)) return "Indiquez une adresse e-mail valide.";
        if (string.IsNullOrWhiteSpace(e.Sujet) || e.Sujet.Trim().Length is < 3 or > 150) return "Indiquez le sujet de votre message (3 à 150 caractères).";
        var text = e.Nachricht?.Trim() ?? string.Empty;
        if (text.Length < NachrichtMin) return "Le message doit contenir au moins 10 caractères.";
        if (text.Length > NachrichtMax) return "Le message est trop long (5 000 caractères au maximum).";
        return null;
    }

    /// <summary>Kontakt des betreuenden Responsable (nur für die Direktion einer Schule).</summary>
    public async Task<ResponsableKontakt?> ResponsableFuerAsync(Guid kontoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var k = await db.Users.AsNoTracking().Where(x => x.Id == kontoId)
            .Select(x => new { x.Rolle, ResponsableId = x.Schule != null ? x.Schule.ResponsableId : null }).FirstOrDefaultAsync(ct);
        if (k is null || !Skoulou.Core.Rechte.Rollen.IstDirektion(k.Rolle) || k.ResponsableId is null) return null;
        var r = await db.Users.AsNoTracking().Where(x => x.Id == k.ResponsableId)
            .Select(x => new { x.Vorname, x.Nachname, x.Email, x.Telefon }).FirstOrDefaultAsync(ct);
        return r is null ? null : new ResponsableKontakt($"{r.Vorname} {r.Nachname}".Trim(), r.Email, r.Telefon, WhatsAppLink(r.Telefon));
    }

    /// <summary>wa.me-Link nur für internationale Nummern (« + » oder « 00 », mindestens 8 Ziffern).</summary>
    public static string? WhatsAppLink(string? telefon)
    {
        var t = telefon?.Trim();
        if (string.IsNullOrEmpty(t) || !(t.StartsWith('+') || t.StartsWith("00", StringComparison.Ordinal))) return null;
        var ziffern = new string(t.Where(char.IsDigit).ToArray());
        if (t.StartsWith("00", StringComparison.Ordinal)) ziffern = ziffern[2..];
        ziffern = ziffern.TrimStart('0');
        return ziffern.Length >= 8 ? "https://wa.me/" + ziffern : null;
    }

    public async Task SendenAsync(KontaktEingabe e, Guid? kontoId, CancellationToken ct = default)
    {
        if (Pruefe(e) is { } fehler) throw new FachFehler(fehler);
        var name = e.Name!.Trim();
        var adresse = e.Email!.Trim();
        var sujet = e.Sujet!.Trim();
        var textHtml = HtmlBereiniger.TextAlsHtml(e.Nachricht!.Trim());
        var support = postmark.CurrentValue.SupportAdresse;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        Guid? schuleId = kontoId is null ? null
            : await db.Users.AsNoTracking().Where(k => k.Id == kontoId).Select(k => k.SchuleId).FirstOrDefaultAsync(ct);
        db.Nachrichten.Add(new Nachricht
        {
            Richtung = NachrichtRichtung.Eingang,
            Von = adresse,
            An = support,
            Betreff = $"[Contact] {sujet}".Length > 300 ? $"[Contact] {sujet}"[..300] : $"[Contact] {sujet}",
            TextHtml = $"<p><strong>{WebUtility.HtmlEncode(name)}</strong> &lt;{WebUtility.HtmlEncode(adresse)}&gt;</p><p>{textHtml}</p>",
            SchuleId = schuleId
        });
        await db.SaveChangesAsync(ct);

        var inhalt = MailLayout.InfoKasten(
                $"<p style=\"margin:0;\"><strong>{WebUtility.HtmlEncode(name)}</strong><br>{WebUtility.HtmlEncode(adresse)}</p>")
            + $"<p style=\"margin:0 0 14px;\">{textHtml}</p>";
        await mails.EinreihenAsync(support, $"[Contact] {sujet}", inhalt, "contact", antwortAn: adresse, ct: ct);
    }
}
