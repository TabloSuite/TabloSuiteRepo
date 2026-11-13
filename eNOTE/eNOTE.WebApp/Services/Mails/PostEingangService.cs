using System.Net.Mail;
using ENote.Core.DataModels.Infrastruktur;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services.Mails;

/// <summary>
/// Posteingang von eNOTE: Antworten und Anfragen an contact@tablosuite.com, die eNOTE betreffen.
/// Alle Apps nutzen dieselbe Adresse; ausgehende Mails tragen « [eNOTE] » im Betreff, Antworten
/// darauf werden vom gemeinsamen Postmark-Webhook (SKOULOU) hierher weitergeleitet.
/// Nur der SuperAdmin liest und beantwortet; Inhalte werden nur als Text gespeichert.
/// </summary>
public class PostEingangService(IDbContextFactory<ApplicationDbContext> factory, EmailService email, ILogger<PostEingangService> logger)
{
    public const int AntwortMax = 5000;

    /// <summary>Zuordnungsregel für eingehende Mails (gleiche Regel im Router von SKOULOU).</summary>
    public static bool BetrifftEnote(string? betreff)
        => betreff?.Contains("enote", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Von den Apps selbst verschickte Mails (Absender = eigene Adresse) nicht als Eingang speichern.</summary>
    public static bool IstEigeneAbsenderAdresse(string? von, params string?[] eigeneAdressen)
    {
        if (string.IsNullOrWhiteSpace(von) || !MailAddress.TryCreate(von.Trim(), out var adresse))
            return false;
        return eigeneAdressen.Any(eigene => !string.IsNullOrWhiteSpace(eigene)
            && string.Equals(adresse.Address, eigene.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Speichert eine eingehende Mail (doppelte Zustellung wird ignoriert). true = neu gespeichert.</summary>
    public async Task<bool> EmpfangenAsync(string absender, string empfaenger, string betreff, string text, string? anbieterId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!string.IsNullOrWhiteSpace(anbieterId) && await db.EingangsMails.AnyAsync(m => m.AnbieterId == anbieterId, ct))
            return false;

        db.EingangsMails.Add(new EingangsMail
        {
            Absender = Kuerze(absender.Trim(), 320),
            Empfaenger = Kuerze(empfaenger.Trim(), 1000),
            Betreff = Kuerze(string.IsNullOrWhiteSpace(betreff) ? "(sans objet)" : betreff.Trim(), 500),
            Text = Kuerze(text.Trim(), 50_000),
            AnbieterId = string.IsNullOrWhiteSpace(anbieterId) ? null : Kuerze(anbieterId.Trim(), 100)
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<EingangsMail>> LadeAsync(Guid admin, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AdminKontenService.PruefeAsync(db, admin, false, ct);
        return await db.EingangsMails.AsNoTracking().OrderByDescending(m => m.EmpfangenUtc).Take(200).ToListAsync(ct);
    }

    public async Task MarkiereGelesenAsync(Guid admin, Guid mailId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AdminKontenService.PruefeAsync(db, admin, false, ct);
        var mail = await db.EingangsMails.FirstOrDefaultAsync(m => m.Id == mailId, ct);
        if (mail is null || mail.IstGelesen) return;
        mail.IstGelesen = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Antwort an den Absender (Betreff « Re: … », « [eNOTE] » setzt der Versand). Fehlermeldung oder null.</summary>
    public async Task<string?> AntwortenAsync(Guid admin, Guid mailId, string? antwort, CancellationToken ct = default)
    {
        var text = (antwort ?? "").Trim();
        if (text.Length < 2) return "Veuillez écrire votre réponse.";
        if (text.Length > AntwortMax) return $"La réponse est trop longue ({AntwortMax} caractères au plus).";

        await using var db = await factory.CreateDbContextAsync(ct);
        await AdminKontenService.PruefeAsync(db, admin, false, ct);
        var mail = await db.EingangsMails.FirstOrDefaultAsync(m => m.Id == mailId, ct);
        if (mail is null) return "Message introuvable.";
        if (!MailAddress.TryCreate(mail.Absender, out var adresse)) return "L'adresse de l'expéditeur n'est pas valide.";

        var betreff = mail.Betreff.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? mail.Betreff : "Re: " + mail.Betreff;
        var zitat = mail.Text.Length > 1500 ? mail.Text[..1500] + " …" : mail.Text;
        var html = TabloSuiteMails.Nachricht(null, "Réponse de l'équipe TabloSuite",
            text + "\n\n— Votre message du " + mail.EmpfangenUtc.ToString("dd.MM.yyyy") + " :\n" + zitat, null, null);
        bool ok;
        try
        {
            ok = await email.SendeAsync(adresse.Address, betreff, html, "message", ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Antwort auf Eingangsmail {Id} konnte nicht versendet werden.", mailId);
            ok = false;
        }
        if (!ok) return "Réponse mise en file d'attente : l'envoi n'a pas encore abouti (voir « Historique », nouvel essai automatique).";

        mail.IstGelesen = true;
        mail.BeantwortetUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return null;
    }

    private static string Kuerze(string text, int max) => text.Length <= max ? text : text[..max];
}
