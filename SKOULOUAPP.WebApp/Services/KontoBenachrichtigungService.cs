using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.Services.Email;
using SkoulouApp.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.WebApp.Services;

public sealed record KontoBenachrichtigungEmpfaenger(Guid Id, string Email, string Vorname, bool IsBlocked, bool IsSelfRegistered);

public sealed class KontoBenachrichtigungService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IEmailService emailService,
    ILogger<KontoBenachrichtigungService> logger)
{
    public async Task<KontoBenachrichtigungEmpfaenger?> LadeEmpfaengerAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AsNoTracking().Where(user => user.Id == userId && user.Email != null)
            .Select(user => new KontoBenachrichtigungEmpfaenger(user.Id, user.Email!, user.Vorname, user.IsBlocked, user.IsSelfRegistered))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task RegistrierungFreigegebenAsync(KontoBenachrichtigungEmpfaenger? vorher, CancellationToken cancellationToken = default)
    {
        if (vorher is not { IsSelfRegistered: true, IsBlocked: true }) return;
        var aktuell = await LadeEmpfaengerAsync(vorher.Id, cancellationToken);
        if (aktuell is null || aktuell.IsBlocked) return;
        await SendenAsync(aktuell.Email, aktuell.Vorname, "Ton compte SKOULOUAPP est activé", "Ton inscription a été validée",
            "<p>Ton inscription a été validée. Tu peux maintenant te connecter à SKOULOUAPP.</p>", cancellationToken);
    }

    public Task RegistrierungAbgelehntAsync(KontoBenachrichtigungEmpfaenger? vorher, CancellationToken cancellationToken = default)
        => vorher is { IsSelfRegistered: true, IsBlocked: true }
            ? SendenAsync(vorher.Email, vorher.Vorname, "Ta demande d'inscription à SKOULOUAPP",
                "Information sur ton inscription", "<p>Ta demande d'inscription n'a pas été acceptée. Ton compte en attente a été supprimé.</p><p>Pour toute question, contacte ton établissement.</p>", cancellationToken)
            : Task.CompletedTask;

    public async Task AdresseModifieeAsync(KontoBenachrichtigungEmpfaenger? vorher, CancellationToken cancellationToken = default)
    {
        if (vorher is null) return;
        var aktuell = await LadeEmpfaengerAsync(vorher.Id, cancellationToken);
        if (aktuell is null || string.Equals(vorher.Email, aktuell.Email, StringComparison.OrdinalIgnoreCase)) return;
        const string subject = "Adresse e-mail de ton compte SKOULOUAPP modifiée";
        const string content = "<p>L'adresse e-mail de ton compte SKOULOUAPP a été modifiée.</p><p>Si tu n'es pas à l'origine de ce changement, contacte immédiatement l'assistance.</p>";
        await SendenAsync(vorher.Email, vorher.Vorname, subject, "Confirmation de sécurité", content, cancellationToken);
        await SendenAsync(aktuell.Email, aktuell.Vorname, subject, "Confirmation de sécurité", content, cancellationToken);
    }

    public async Task PaiementEnregistreAsync(Guid zahlungId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var zahlung = await db.RatenZahlungen.AsNoTracking().Where(item => item.Id == zahlungId && item.FinanzBuchungID != null)
            .Select(item => new { item.Student.Email, item.Student.Vorname, item.Betrag, item.ZahlungsArt, item.Wirkungsdatum })
            .FirstOrDefaultAsync(cancellationToken);
        if (zahlung is null || zahlung.Betrag <= 0) return;
        var betrag = System.Net.WebUtility.HtmlEncode(zahlung.Betrag.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")));
        var datum = System.Net.WebUtility.HtmlEncode(zahlung.Wirkungsdatum.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture));
        await SendenAsync(zahlung.Email, zahlung.Vorname, "Paiement scolaire enregistré – SKOULOUAPP", "Confirmation de paiement",
            $"<p>Un paiement scolaire de <strong>{betrag} FCFA</strong> a été enregistré le {datum}.</p><p>Type de paiement : {(zahlung.ZahlungsArt == ZahlungsArt.Cash ? "paiement intégral" : "versement")}. Consulte ton établissement pour le suivi de ton solde.</p>", cancellationToken);
    }

    public async Task<bool> IstAboBezahltAsync(Guid studentId, Guid klasseId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.LevelEnrollments.AsNoTracking()
            .AnyAsync(item => item.StudentId == studentId && item.KlasseId == klasseId && item.IstAktuell && item.IsPaid, cancellationToken);
    }

    public async Task AboPaiementEnregistreAsync(Guid studentId, Guid klasseId, bool warVorherBezahlt, CancellationToken cancellationToken = default)
    {
        if (warVorherBezahlt) return;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var abo = await db.LevelEnrollments.AsNoTracking()
            .Where(item => item.StudentId == studentId && item.KlasseId == klasseId && item.IstAktuell && item.IsPaid && item.PaidAtUtc != null)
            .Select(item => new { item.Student.Email, item.Student.Vorname, item.PriceAtEnrollment, item.Id })
            .FirstOrDefaultAsync(cancellationToken);
        if (abo is null || abo.PriceAtEnrollment <= 0 || !await db.FinanzBuchungen.AsNoTracking()
            .AnyAsync(buchung => buchung.HerkunftReferenzID == abo.Id && buchung.Richtung == FinanzRichtung.Einnahme
                && buchung.Kategorie.SystemCode == FinanzSystemCode.SkoulouAppAboZahlung, cancellationToken)) return;

        var betrag = System.Net.WebUtility.HtmlEncode(abo.PriceAtEnrollment.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")));
        await SendenAsync(abo.Email, abo.Vorname, "Abonnement SKOULOUAPP enregistré comme payé", "Confirmation d'abonnement",
            $"<p>L'abonnement SKOULOUAPP de ta classe a été enregistré comme payé pour <strong>{betrag} FCFA</strong>.</p><p>Tu peux consulter tes contenus disponibles dans ton espace.</p>", cancellationToken);
    }

    private async Task SendenAsync(string? email, string? vorname, string subject, string heading, string content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email)) return;
        var name = System.Net.WebUtility.HtmlEncode(vorname ?? string.Empty);
        var html = EmailTemplates.Wrap(heading, $"<p>Bonjour {name},</p>{content}<p>À bientôt,<br/>L'équipe SKOULOUAPP</p>");
        try
        {
            var result = await emailService.SendAsync(new EmailRecipient(email, vorname),
                new EmailMessage { Subject = subject, HtmlBody = html, TextBody = EmailTemplates.ToPlainText(html) }, cancellationToken);
            if (!result.Succeeded)
                logger.LogWarning("Benachrichtigungsmail an {Email} fehlgeschlagen: {Error}", email, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Benachrichtigungsmail an {Email} fehlgeschlagen.", email);
        }
    }
}
