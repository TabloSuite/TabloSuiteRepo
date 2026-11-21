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
    Mails.MailBenachrichtigungen? mails = null, TimeProvider? zeit = null)
{
    private readonly TimeProvider uhr = zeit ?? ENote.Core.Services.AppUhr.Provider;

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
        int VerkaeufeMonat = 0,
        bool KannErstatten = false,
        bool KannSichAnmelden = false,
        bool HatPasswort = false,
        bool KannKundenAktenFuehren = false);

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
        var monatsAnfang = new DateTime(uhr.GetUtcNow().UtcDateTime.Year, uhr.GetUtcNow().UtcDateTime.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var letzteKasse = await db.KassenSitzungen.AsNoTracking()
            .Where(k => k.SalonId == salonId && userIds.Contains(k.KassiererId))
            .GroupBy(k => k.KassiererId)
            .Select(g => new { g.Key, Letzte = g.Max(k => k.GestartetUtc) })
            .ToDictionaryAsync(x => x.Key, x => x.Letzte, cancellationToken);
        // « Qui encaisse ? » (17.11.2026): Verkäufe zählen für die Person, die den Bon gebucht hat (ältere Bons ohne
        // Kassierer: Person, die die Kasse geöffnet hat). Wer nur in eine offene Kasse eingestiegen ist, zählt so auch.
        var verkaeufe = await db.Verkaeufe.AsNoTracking()
            .Where(v => v.SalonId == salonId && v.ZeitUtc >= monatsAnfang && v.StornoVonVerkaufId == null)
            .GroupBy(v => v.KassiererId ?? v.KassenSitzung.KassiererId)
            .Select(g => new { g.Key, Anzahl = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Anzahl, cancellationToken);
        // « Dernière caisse »: auch Kassieren in einer von jemand anderem geöffneten Kasse zählt als Aktivität.
        var letzteBons = await db.Verkaeufe.AsNoTracking()
            .Where(v => v.SalonId == salonId && v.KassiererId != null && userIds.Contains(v.KassiererId.Value))
            .GroupBy(v => v.KassiererId!.Value)
            .Select(g => new { g.Key, Letzte = g.Max(v => v.ZeitUtc) })
            .ToDictionaryAsync(x => x.Key, x => x.Letzte, cancellationToken);
        foreach (var (id, zeit) in letzteBons)
            if (!letzteKasse.TryGetValue(id, out var bisher) || zeit > bisher) letzteKasse[id] = zeit;

        return zuordnungen.Select(us => new TeamMitglied(
            us.Id, us.AppUserId, us.AppUser.FullName, us.AppUser.Email ?? string.Empty,
            us.KassenPinHash is not null,
            us.KannFinanzenSehen, us.KannLeistungenVerwalten, us.KannRabatteVerwalten,
            besitzerIds.Contains(us.AppUserId),
            us.AppUser.UserName,
            letzteKasse.TryGetValue(us.AppUserId, out var letzte) ? letzte : null,
            verkaeufe.GetValueOrDefault(us.AppUserId),
            us.KannErstatten,
            us.KannSichAnmelden,
            us.AppUser.PasswordHash != null,
            us.KannKundenAktenFuehren)).ToList();
    }

    /// <summary>
    /// Legt einen Kassierer an (Identity-Konto + Rolle) und ordnet ihn dem Salon mit einer Kassen-PIN zu. Die E-Mail ist nur
    /// Kontaktadresse und darf mehrfach verwendet werden. Seit 17.11.2026 standardmäßig OHNE Passwort: Der Mitarbeiter kassiert
    /// am Gerät des Besitzers mit Name + PIN. Mit <paramref name="initialPasswort"/> erhält er zugleich « Connexion personnelle ».
    /// </summary>
    /// <summary>« prenom.nom » ohne Akzente (nur a–z, 0–9, Punkt); belegt → « prenom.nom2 », « prenom.nom3 » …</summary>
    private async Task<string> FreierPseudonymAsync(string? vorname, string? nachname)
    {
        static string Teil(string? t)
        {
            var normal = (t ?? string.Empty).Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var c in normal)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            }
            return sb.ToString().Trim('-');
        }
        var basis = string.Join('.', new[] { Teil(vorname), Teil(nachname) }.Where(t => t.Length > 0));
        if (basis.Length == 0) return string.Empty;
        if (basis.Length > 40) basis = basis[..40];
        for (var i = 1; i < 1000; i++)
        {
            var kandidat = i == 1 ? basis : basis + i;
            if (await userManager.FindByNameAsync(kandidat) is null) return kandidat;
        }
        return basis + "." + Guid.NewGuid().ToString("N")[..6];
    }

    public async Task<(bool Erfolg, string? Fehler, AppUser? Kassierer)> LegeKassiererAnAsync(
        Guid salonId,
        string vorname,
        string nachname,
        string pseudonym,
        string email,
        string? initialPasswort,
        string kassenPin,
        CancellationToken cancellationToken = default)
    {
        if (!KassenPinService.IstGueltigesPinFormat(kassenPin))
        {
            return (false, "Le code PIN doit contenir 4 à 6 chiffres.", null);
        }

        // Länge begrenzen (Pflicht prüft weiter unten der Pseudonym-Vorschlag wie bisher).
        if (KontoProfilService.PruefeNamen(vorname, nachname, pflicht: false) is { } namensFehler)
        {
            return (false, namensFehler, null);
        }

        var bereinigtPseudonym = (pseudonym ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(bereinigtPseudonym))
        {
            // 18.11.2026: Mitarbeitende ohne Passwort brauchen keinen selbst erdachten Pseudonym –
            // eNOTE schlägt « prenom.nom » vor (bei Kollision mit Zahl), wie SKOULOU.
            bereinigtPseudonym = await FreierPseudonymAsync(vorname, nachname);
            if (bereinigtPseudonym.Length == 0)
            {
                return (false, "Veuillez saisir le prénom et le nom.", null);
            }
        }

        // Login = Pseudonym (eindeutig); E-Mail ist nur Kontaktadresse und darf mehrfach sein.
        if (await userManager.FindByNameAsync(bereinigtPseudonym) is not null)
        {
            return (false, "Ce pseudonyme est déjà utilisé.", null);
        }

        // E-Mail freiwillig (viele Angestellte haben keine); leer = null, nicht "".
        var bereinigtEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        var kassierer = new AppUser
        {
            UserName = bereinigtPseudonym,
            Email = bereinigtEmail,
            Vorname = vorname.Trim(),
            Nachname = nachname.Trim(),
            FullName = $"{vorname.Trim()} {nachname.Trim()}",
            EmailConfirmed = true
        };

        var mitAnmeldung = !string.IsNullOrWhiteSpace(initialPasswort);
        var erstellen = mitAnmeldung
            ? await userManager.CreateAsync(kassierer, initialPasswort!)
            : await userManager.CreateAsync(kassierer);
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
            KassenPinHash = KassenPinService.HashPin(kassenPin),
            KannSichAnmelden = mitAnmeldung
        });
        await db.SaveChangesAsync(cancellationToken);

        if (mails is not null && bereinigtEmail is not null)
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
        bool kannErstatten = false,
        bool kannKundenAktenFuehren = false,
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
        zuordnung.KannErstatten = kannErstatten;
        zuordnung.KannKundenAktenFuehren = kannKundenAktenFuehren;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// « Connexion personnelle » ein- oder ausschalten. Einschalten: Hat das Konto noch kein Passwort, ist eines nötig; mit
    /// <paramref name="neuesPasswort"/> wird es (neu) gesetzt. Ausschalten: Der Mitarbeiter wird sofort abgemeldet.
    /// </summary>
    public async Task<(bool Erfolg, string? Fehler)> SetzeAnmeldungAsync(
        Guid userSalonId, bool erlaubt, string? neuesPasswort, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var zuordnung = await db.UserSalons.Include(us => us.AppUser).FirstOrDefaultAsync(us => us.Id == userSalonId, cancellationToken);
        if (zuordnung is null) return (false, "Membre introuvable.");
        if (await db.UserRoles.AnyAsync(ur => ur.UserId == zuordnung.AppUserId
                && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.Besitzer), cancellationToken))
            return (false, "Le propriétaire se connecte toujours avec son compte.");

        if (erlaubt)
        {
            var konto = await userManager.FindByIdAsync(zuordnung.AppUserId.ToString());
            if (konto is null) return (false, "Membre introuvable.");
            if (!string.IsNullOrWhiteSpace(neuesPasswort))
            {
                if (await userManager.HasPasswordAsync(konto))
                {
                    var entfernt = await userManager.RemovePasswordAsync(konto);
                    if (!entfernt.Succeeded) return (false, string.Join(" ", entfernt.Errors.Select(e => e.Description)));
                }
                var gesetzt = await userManager.AddPasswordAsync(konto, neuesPasswort.Trim());
                if (!gesetzt.Succeeded) return (false, string.Join(" ", gesetzt.Errors.Select(e => e.Description)));
            }
            else if (!await userManager.HasPasswordAsync(konto))
            {
                return (false, "Choisissez un mot de passe pour la connexion personnelle.");
            }
            zuordnung.KannSichAnmelden = true;
        }
        else
        {
            zuordnung.KannSichAnmelden = false;
            // Laufende Anmeldung beenden (Cookie wird bei der nächsten Anfrage abgewiesen).
            zuordnung.AppUser.ActiveSessionId = null;
            zuordnung.AppUser.UpdatedAtUtc = uhr.GetUtcNow().UtcDateTime;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
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
        // Eine eigene Anmeldung dieses Mitarbeiters endet sofort (kein Zugriff mehr auf den Laden).
        if (await db.Users.FirstOrDefaultAsync(u => u.Id == zuordnung.AppUserId, cancellationToken) is { } konto
            && await LadenZugriff.IstMitarbeiterKontoAsync(db, konto.Id, cancellationToken))
        {
            konto.ActiveSessionId = null;
        }
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
