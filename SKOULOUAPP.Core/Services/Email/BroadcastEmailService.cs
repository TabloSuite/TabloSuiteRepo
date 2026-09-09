using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HalloApp.Core.Services.Email;

/// <summary>
/// Zielgruppe für einen E-Mail-Broadcast.
/// </summary>
public enum BroadcastAudience
{
    /// <summary>Alle Schulleiter (aller oder einer bestimmten Schule).</summary>
    SchoolLeaders = 0,

    /// <summary>Alle Lehrer (aller oder einer bestimmten Schule).</summary>
    Teachers = 1,

    /// <summary>Alle Schüler (aller oder einer bestimmten Schule).</summary>
    Students = 2,

    /// <summary>Alle Nutzer (Schulleiter, Lehrer, Schüler).</summary>
    Everyone = 3,

    /// <summary>
    /// Nur die manuell eingegebenen Adressen. Rolle und Schule werden ignoriert;
    /// die Empfänger müssen nicht in der Datenbank existieren.
    /// </summary>
    ManualAddresses = 4
}

/// <summary>
/// Kriterien für die Empfängerauswahl eines Broadcasts.
/// </summary>
public sealed class BroadcastCriteria
{
    public BroadcastAudience Audience { get; set; } = BroadcastAudience.Students;

    /// <summary>Optionale Einschränkung auf eine bestimmte Schule (null = alle Schulen).</summary>
    public Guid? SchoolId { get; set; }

    /// <summary>Optionale Einschränkung auf einzelne, konkret ausgewählte Nutzer.</summary>
    public IReadOnlyCollection<Guid>? SpecificUserIds { get; set; }

    /// <summary>
    /// Zusätzliche, frei eingegebene E-Mail-Adressen. Diese werden immer angeschrieben,
    /// unabhängig davon, ob sie zu einem registrierten Nutzer gehören.
    /// </summary>
    public IReadOnlyCollection<string>? ZusaetzlicheAdressen { get; set; }
}

/// <summary>
/// Löst Empfänger anhand von Rolle/Schule auf und versendet zielgerichtete Massen-E-Mails.
/// </summary>
public sealed class BroadcastEmailService(IHalloAppDbContext dbContext, IEmailService emailService)
{
    /// <summary>Ermittelt die passenden Empfänger für die Kriterien (ohne zu senden).</summary>
    public async Task<IReadOnlyList<EmailRecipient>> ResolveRecipientsAsync(BroadcastCriteria criteria, CancellationToken cancellationToken = default)
    {
        var manuelle = (criteria.ZusaetzlicheAdressen ?? [])
            .Select(a => a?.Trim())
            .Where(a => !string.IsNullOrWhiteSpace(a) && IstPlausibleEmail(a!))
            .Select(a => new EmailRecipient(a!))
            .ToList();

        // Bei reiner Direkteingabe wird die Datenbank gar nicht abgefragt.
        if (criteria.Audience == BroadcastAudience.ManualAddresses)
        {
            return Vereinheitlichen(manuelle);
        }

        var query = dbContext.Users
            .AsNoTracking()
            .Where(u => !u.IsBlocked && u.Email != null && u.Email != "");

        if (criteria.SpecificUserIds is { Count: > 0 })
        {
            var ids = criteria.SpecificUserIds.ToHashSet();
            query = query.Where(u => ids.Contains(u.Id));
        }
        else
        {
            var roleNames = RoleNamesFor(criteria.Audience);
            if (roleNames.Length > 0)
            {
                var roleIds = await dbContext.Roles
                    .AsNoTracking()
                    .Where(r => r.Name != null && roleNames.Contains(r.Name))
                    .Select(r => r.Id)
                    .ToListAsync(cancellationToken);

                query = query.Where(u => u.UserRoles.Any(ur => roleIds.Contains(ur.RoleId)));
            }

            if (criteria.SchoolId is Guid schoolId)
            {
                query = query.Where(u => u.UserSchools.Any(us => us.SchoolId == schoolId));
            }
        }

        var users = await query
            .Select(u => new { u.Email, u.FullName, u.Vorname, u.Nachname })
            .ToListAsync(cancellationToken);

        return Vereinheitlichen(users
            .Select(u => new EmailRecipient(
                u.Email!,
                string.IsNullOrWhiteSpace(u.FullName) ? $"{u.Vorname} {u.Nachname}".Trim() : u.FullName))
            .Concat(manuelle));
    }

    /// <summary>Entfernt Dubletten (Adresse, Groß-/Kleinschreibung wird ignoriert).</summary>
    private static List<EmailRecipient> Vereinheitlichen(IEnumerable<EmailRecipient> recipients)
        => recipients
            .GroupBy(r => r.Email, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

    /// <summary>Grobe Syntaxprüfung, damit offensichtlich ungültige Eingaben nicht versendet werden.</summary>
    private static bool IstPlausibleEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 && at < email.Length - 1 && email.LastIndexOf('.') > at;
    }

    /// <summary>Sendet die Nachricht an alle passenden Empfänger.</summary>
    public async Task<EmailBatchResult> SendBroadcastAsync(BroadcastCriteria criteria, EmailMessage message, CancellationToken cancellationToken = default)
    {
        var recipients = await ResolveRecipientsAsync(criteria, cancellationToken);
        if (recipients.Count == 0)
        {
            return new EmailBatchResult(0, 0, [
                criteria.Audience == BroadcastAudience.ManualAddresses
                    ? "Aucune adresse e-mail valide n'a été saisie."
                    : "Aucun destinataire correspondant n'a été trouvé."
            ]);
        }

        return await emailService.SendBatchAsync(recipients, message, cancellationToken);
    }

    private static string[] RoleNamesFor(BroadcastAudience audience) => audience switch
    {
        BroadcastAudience.SchoolLeaders => [Rollen.SchulLeiter],
        BroadcastAudience.Teachers => [Rollen.Lehrer],
        BroadcastAudience.Students => [Rollen.Schueler],
        BroadcastAudience.Everyone => [Rollen.SchulLeiter, Rollen.Lehrer, Rollen.Schueler],        _ => []
    };
}
