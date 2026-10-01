using HalloApp.Core.DataModels.Email;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.Services.Email;
using HalloApp.Core.Services.Text;
using HalloApp.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace HalloApp.WebApp.Services;

public sealed class MailboxService(IDbContextFactory<ApplicationDbContext> dbContextFactory, IEmailService emailService)
{
    public async Task<IReadOnlyList<MailNachricht>> LadeAsync(Guid userId, bool istAdmin, Guid? produktOwnerFilter, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        istAdmin = await IstAdminAsync(db, userId, cancellationToken);
        if (!istAdmin && !await IstOwnerAsync(db, userId, cancellationToken)) return [];
        var query = db.MailNachrichten.AsNoTracking().AsQueryable();
        if (!istAdmin)
        {
            var schulIds = await db.UserSchools.AsNoTracking()
                .Where(link => link.AppUserId == userId && link.AppUser.UserRoles.Any(role => role.Role.Name == Rollen.ProduktOwner))
                .Select(link => link.SchoolId).ToListAsync(cancellationToken);
            query = query.Where(mail => mail.SchulID.HasValue && schulIds.Contains(mail.SchulID.Value));
        }
        else if (produktOwnerFilter.HasValue)
        {
            var schulIds = await db.UserSchools.AsNoTracking()
                .Where(link => link.AppUserId == produktOwnerFilter.Value && link.AppUser.UserRoles.Any(role => role.Role.Name == Rollen.ProduktOwner))
                .Select(link => link.SchoolId).ToListAsync(cancellationToken);
            query = query.Where(mail => mail.SchulID.HasValue && schulIds.Contains(mail.SchulID.Value));
        }

        return await query.OrderByDescending(mail => mail.ErstelltAmUtc).Take(200).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(Guid Id, string Name)>> SchulenAsync(Guid userId, bool istAdmin, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        istAdmin = await IstAdminAsync(db, userId, cancellationToken);
        if (!istAdmin && !await IstOwnerAsync(db, userId, cancellationToken)) return [];
        return await (istAdmin ? db.Schools.AsNoTracking() : db.Schools.AsNoTracking().Where(school => school.UserSchools.Any(link => link.AppUserId == userId)))
            .OrderBy(school => school.Name).Select(school => new ValueTuple<Guid, string>(school.Id, school.Name)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(Guid Id, string Name)>> ProduktOwnerAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IstAdminAsync(db, userId, cancellationToken)) return [];
        return await db.Users.AsNoTracking().Where(user => user.UserRoles.Any(role => role.Role.Name == Rollen.ProduktOwner))
            .OrderBy(user => user.FullName).Select(user => new ValueTuple<Guid, string>(user.Id, user.FullName)).ToListAsync(cancellationToken);
    }

    public async Task<(bool Succeeded, string Message)> SendenAsync(Guid userId, bool istAdmin, Guid? schulId, string empfaenger, string betreff, string inhalt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(empfaenger) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(empfaenger.Trim())
            || string.IsNullOrWhiteSpace(betreff) || betreff.Length > 300 || HtmlSanitizer.ToPlainText(inhalt).Length < 1)
        {
            return (false, "Vérifie l'adresse, l'objet et le message.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        istAdmin = await IstAdminAsync(db, userId, cancellationToken);
        if (!istAdmin && !await IstOwnerAsync(db, userId, cancellationToken)) return (false, "Accès refusé.");
        if (!istAdmin && (!schulId.HasValue || !await db.UserSchools.AnyAsync(link => link.AppUserId == userId && link.SchoolId == schulId.Value
            && link.AppUser.UserRoles.Any(role => role.Role.Name == Rollen.ProduktOwner), cancellationToken)))
        {
            return (false, "Cette école ne vous est pas attribuée.");
        }
        if (!istAdmin && !await db.Users.AsNoTracking().AnyAsync(user => user.Email == empfaenger.Trim()
            && user.UserSchools.Any(link => link.SchoolId == schulId), cancellationToken))
        {
            return (false, "Le destinataire ne fait pas partie de cette école.");
        }
        if (schulId.HasValue && !await db.Schools.AnyAsync(school => school.Id == schulId, cancellationToken))
        {
            return (false, "École introuvable.");
        }

        var result = await emailService.SendAsync(new EmailRecipient(empfaenger.Trim()), new EmailMessage
        {
            Subject = betreff.Trim(), HtmlBody = EmailTemplates.Wrap(betreff.Trim(), HtmlSanitizer.Sanitize(inhalt))
        }, cancellationToken);
        if (!result.Succeeded)
        {
            return (false, result.ErrorMessage ?? "L'envoi a échoué.");
        }

        db.MailNachrichten.Add(new MailNachricht
        {
            SchulID = schulId, GesendetVonID = userId, Absender = "contact.skoulou@etsetech.com",
            Empfaenger = empfaenger.Trim(), Betreff = betreff.Trim(), Inhalt = HtmlSanitizer.Sanitize(inhalt)
        });
        await db.SaveChangesAsync(cancellationToken);
        return (true, "Message envoyé.");
    }

    public async Task EmpfangenAsync(string absender, string empfaenger, string betreff, string inhalt, string? postmarkId, Guid? schulId = null, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(postmarkId) && await db.MailNachrichten.AnyAsync(mail => mail.PostmarkMessageID == postmarkId, cancellationToken)) return;
        schulId = null;
        db.MailNachrichten.Add(new MailNachricht
        {
            SchulID = null,
            Absender = absender[..Math.Min(absender.Length, 256)], Empfaenger = empfaenger[..Math.Min(empfaenger.Length, 256)],
            Betreff = betreff[..Math.Min(betreff.Length, 300)], Inhalt = HtmlSanitizer.Sanitize(inhalt),
            PostmarkMessageID = string.IsNullOrWhiteSpace(postmarkId) ? null : postmarkId[..Math.Min(postmarkId.Length, 100)], IstEingehend = true
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ZuordnenAsync(Guid adminId, Guid mailId, Guid? schulId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IstAdminAsync(db, adminId, cancellationToken)) return false;
        var mail = await db.MailNachrichten.FindAsync([mailId], cancellationToken);
        if (mail is null || (schulId.HasValue && !await db.Schools.AnyAsync(school => school.Id == schulId.Value, cancellationToken))) return false;
        mail.SchulID = schulId;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static Task<bool> IstAdminAsync(ApplicationDbContext db, Guid userId, CancellationToken cancellationToken)
        => db.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.UserRoles.Any(role => role.Role.Name == Rollen.SuperAdmin), cancellationToken);

    private static Task<bool> IstOwnerAsync(ApplicationDbContext db, Guid userId, CancellationToken cancellationToken)
        => db.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.UserRoles.Any(role => role.Role.Name == Rollen.ProduktOwner), cancellationToken);
}
