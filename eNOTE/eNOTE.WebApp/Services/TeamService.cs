using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Verwaltet das Team eines Salons: Kassierer anlegen, Kassen-PIN vergeben,
/// delegierbare Rechte setzen und Salon-Zuordnungen entfernen.
/// </summary>
public class TeamService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    UserManager<AppUser> userManager,
    ILogger<TeamService> logger,
    Mails.MailBenachrichtigungen? mails = null)
{
    public record TeamMitglied(
        Guid UserSalonId,
        Guid UserId,
        string Name,
        string Email,
        bool HatPin,
        bool KannFinanzenSehen,
        bool KannLeistungenVerwalten,
        bool KannRabatteVerwalten,
        bool IstBesitzer,
        string? Pseudonym = null,
        DateTime? LetzteKasseUtc = null,
        int VerkaeufeMonat = 0);

    /// <summary>Alle Mitglieder eines Salons inklusive ihrer Rechte.</summary>
    public async Task<List<TeamMitglied>> GetTeamAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var zuordnungen = await db.UserSalons
            .Include(us => us.AppUser)
            .AsNoTracking()
            .Where(us => us.SalonId == salonId)
            .OrderBy(us => us.AppUser.FullName)
            .ToListAsync(cancellationToken);

        // Rollen über denselben Factory-Context lesen (nicht über den circuit-weiten UserManager,
        // dessen geteilter DbContext bei parallelen Abfragen Concurrency-Fehler auslöst).
        var userIds = zuordnungen.Select(us => us.AppUserId).ToList();
        var besitzerIds = await db.UserRoles
            .Where(ur => userIds.Contains(ur.UserId)
                && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.Besitzer))
            .Select(ur => ur.UserId)
            .ToHashSetAsync(cancellationToken);

        // Aktivität an der Kasse: letzte Sitzung und Verkäufe im laufenden Monat (ohne Rückerstattungen).
        var monatsAnfang = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var letzteKasse = await db.KassenSitzungen.AsNoTracking()
            .Where(k => k.SalonId == salonId && userIds.Contains(k.KassiererId))
            .GroupBy(k => k.KassiererId)
            .Select(g => new { g.Key, Letzte = g.Max(k => k.GestartetUtc) })
            .ToDictionaryAsync(x => x.Key, x => x.Letzte, cancellationToken);
        var verkaeufe = await db.Verkaeufe.AsNoTracking()
            .Where(v => v.SalonId == salonId && v.ZeitUtc >= monatsAnfang && v.StornoVonVerkaufId == null)
            .GroupBy(v => v.KassenSitzung.KassiererId)
            .Select(g => new { g.Key, Anzahl = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Anzahl, cancellationToken);

        return zuordnungen.Select(us => new TeamMitglied(
            us.Id, us.AppUserId, us.AppUser.FullName, us.AppUser.Email ?? string.Empty,
            us.KassenPinHash is not null,
            us.KannFinanzenSehen, us.KannLeistungenVerwalten, us.KannRabatteVerwalten,
            besitzerIds.Contains(us.AppUserId),
            us.AppUser.UserName,
            letzteKasse.TryGetValue(us.AppUserId, out var letzte) ? letzte : null,
            verkaeufe.GetValueOrDefault(us.AppUserId))).ToList();
    }

    /// <summary>
    /// Legt einen Kassierer an (Identity-Konto + Rolle) und ordnet ihn dem Salon
    /// mit einer Kassen-PIN zu. Login ist das eindeutige Pseudonym; die E-Mail ist
    /// nur Kontaktadresse und darf mehrfach verwendet werden. Das Passwort ist ein Initialpasswort.
    /// </summary>
    public async Task<(bool Erfolg, string? Fehler, AppUser? Kassierer)> LegeKassiererAnAsync(
        Guid salonId,
        string vorname,
        string nachname,
        string pseudonym,
        string email,
        string initialPasswort,
        string kassenPin,
        CancellationToken cancellationToken = default)
    {
        if (!KassenPinService.IstGueltigesPinFormat(kassenPin))
        {
            return (false, "Le code PIN doit contenir 4 à 6 chiffres.", null);
        }

        var bereinigtPseudonym = pseudonym.Trim();
        if (string.IsNullOrWhiteSpace(bereinigtPseudonym))
        {
            return (false, "Veuillez saisir un pseudonyme.", null);
        }

        // Login = Pseudonym (eindeutig); E-Mail ist nur Kontaktadresse und darf mehrfach sein.
        if (await userManager.FindByNameAsync(bereinigtPseudonym) is not null)
        {
            return (false, "Ce pseudonyme est déjà utilisé.", null);
        }

        var bereinigtEmail = email.Trim();
        var kassierer = new AppUser
        {
            UserName = bereinigtPseudonym,
            Email = bereinigtEmail,
            Vorname = vorname.Trim(),
            Nachname = nachname.Trim(),
            FullName = $"{vorname.Trim()} {nachname.Trim()}",
            EmailConfirmed = true
        };

        var erstellen = await userManager.CreateAsync(kassierer, initialPasswort);
        if (!erstellen.Succeeded)
        {
            var fehler = string.Join(" ", erstellen.Errors.Select(e => e.Description));
            logger.LogWarning("Kassierer-Anlage fehlgeschlagen für {Email}: {Fehler}", bereinigtEmail, fehler);
            return (false, string.IsNullOrWhiteSpace(fehler) ? "La création a échoué." : fehler, null);
        }

        await userManager.AddToRoleAsync(kassierer, AppRollen.Kassierer);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.UserSalons.Add(new UserSalon
        {
            AppUserId = kassierer.Id,
            SalonId = salonId,
            KassenPinHash = KassenPinService.HashPin(kassenPin)
        });
        await db.SaveChangesAsync(cancellationToken);

        if (mails is not null)
            await mails.KassiererAngelegtAsync(bereinigtEmail, kassierer.Vorname, kassierer.UserName ?? "", salonId, cancellationToken);

        return (true, null, kassierer);
    }

    /// <summary>Setzt/ändert die Kassen-PIN einer Salon-Zuordnung.</summary>
    public async Task<(bool Erfolg, string? Fehler)> SetzeKassenPinAsync(
        Guid userSalonId, string neuePin, CancellationToken cancellationToken = default)
        => await SetzePinAsync(userSalonId, neuePin, istBesitzerPin: false, cancellationToken);

    /// <summary>Setzt/ändert die Besitzer-PIN einer Salon-Zuordnung (zum Freischalten der Verwaltung).</summary>
    public async Task<(bool Erfolg, string? Fehler)> SetzeBesitzerPinAsync(
        Guid userSalonId, string neuePin, CancellationToken cancellationToken = default)
        => await SetzePinAsync(userSalonId, neuePin, istBesitzerPin: true, cancellationToken);

    private async Task<(bool Erfolg, string? Fehler)> SetzePinAsync(
        Guid userSalonId, string neuePin, bool istBesitzerPin, CancellationToken cancellationToken)
    {
        if (!KassenPinService.IstGueltigesPinFormat(neuePin))
        {
            return (false, "Le code PIN doit contenir 4 à 6 chiffres.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var zuordnung = await db.UserSalons.FirstOrDefaultAsync(us => us.Id == userSalonId, cancellationToken);
        if (zuordnung is null)
        {
            return (false, "Membre introuvable.");
        }

        if (istBesitzerPin)
        {
            zuordnung.BesitzerPinHash = KassenPinService.HashPin(neuePin);
        }
        else
        {
            zuordnung.KassenPinHash = KassenPinService.HashPin(neuePin);
        }

        zuordnung.PinFehlversuche = 0;
        zuordnung.PinGesperrtBisUtc = null;
        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    /// <summary>Setzt die delegierbaren Rechte eines Mitglieds.</summary>
    public async Task<bool> SetzeRechteAsync(
        Guid userSalonId,
        bool kannFinanzenSehen,
        bool kannLeistungenVerwalten,
        bool kannRabatteVerwalten,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var zuordnung = await db.UserSalons.FirstOrDefaultAsync(us => us.Id == userSalonId, cancellationToken);
        if (zuordnung is null)
        {
            return false;
        }

        zuordnung.KannFinanzenSehen = kannFinanzenSehen;
        zuordnung.KannLeistungenVerwalten = kannLeistungenVerwalten;
        zuordnung.KannRabatteVerwalten = kannRabatteVerwalten;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Entfernt ein Mitglied aus dem Salon (das Identity-Konto bleibt bestehen).</summary>
    public async Task<bool> EntferneMitgliedAsync(Guid userSalonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var zuordnung = await db.UserSalons.FirstOrDefaultAsync(us => us.Id == userSalonId, cancellationToken);
        if (zuordnung is null)
        {
            return false;
        }

        db.UserSalons.Remove(zuordnung);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
