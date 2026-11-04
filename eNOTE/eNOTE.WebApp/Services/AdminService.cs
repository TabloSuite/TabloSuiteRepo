using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// SuperAdmin-/ProduktOwner-Verwaltung: Salons freigeben/ablehnen, Übersicht aller
/// Läden inklusive Teamgröße und Umsatz, Plattformpreise und Abo-Verwaltung.
/// </summary>
public class AdminService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    UserManager<AppUser> userManager,
    ILogger<AdminService> logger)
{
    public record SalonUebersicht(
        Guid SalonId,
        string Name,
        string? Standort,
        string? Land,
        bool IstFreigegeben,
        bool IstAktiv,
        bool HatProbezeit,
        int AnzahlMitglieder,
        decimal GesamtUmsatz,
        DateTime CreatedAtUtc,
        string? BesitzerName);

    public async Task<List<SalonUebersicht>> GetSalonUebersichtAsync(CancellationToken cancellationToken = default)
        => await GetSalonUebersichtGefiltertAsync(null, null, null, null, cancellationToken);

    /// <summary>
    /// Gefilterte Salon-Übersicht für große Datenmengen. Alle Parameter optional.
    /// suchBegriff filtert Name/Standort (case-insensitiv), nurFreigegebene/nurWartende
    /// schränken den Status ein, land filtert exakt, limit begrenzt die Trefferzahl.
    /// </summary>
    public async Task<List<SalonUebersicht>> GetSalonUebersichtGefiltertAsync(
        string? suchBegriff,
        bool? nurFreigegebene,
        bool? nurWartende,
        string? land,
        CancellationToken cancellationToken = default,
        int limit = 200)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var abfrage = db.Salons.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(suchBegriff))
        {
            var s = suchBegriff.Trim();
            abfrage = abfrage.Where(x => x.Name.Contains(s) || (x.Standort != null && x.Standort.Contains(s)));
        }

        if (nurFreigegebene == true)
        {
            abfrage = abfrage.Where(x => x.IstFreigegeben);
        }

        if (nurWartende == true)
        {
            abfrage = abfrage.Where(x => !x.IstFreigegeben && x.IstAktiv);
        }

        if (!string.IsNullOrWhiteSpace(land))
        {
            abfrage = abfrage.Where(x => x.Land == land);
        }

        var salons = await abfrage
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var ids = salons.Select(s => s.Id).ToList();
        var mitglieder = await db.UserSalons.AsNoTracking()
            .Where(us => ids.Contains(us.SalonId))
            .GroupBy(us => us.SalonId)
            .Select(g => new { g.Key, Anzahl = g.Count() })
            .ToListAsync(cancellationToken);
        var umsatz = await db.Verkaeufe.AsNoTracking()
            .Where(v => ids.Contains(v.SalonId))
            .GroupBy(v => v.SalonId)
            .Select(g => new { g.Key, Summe = g.Sum(v => v.GesamtSumme) })
            .ToListAsync(cancellationToken);

        var besitzerNamen = await db.UserSalons.AsNoTracking()
            .Where(us => ids.Contains(us.SalonId) && us.BesitzerPinHash != null)
            .GroupBy(us => us.SalonId)
            .Select(g => new { g.Key, Name = g.Select(us => us.AppUser.FullName).FirstOrDefault() })
            .ToListAsync(cancellationToken);

        return salons.Select(s => new SalonUebersicht(
            s.Id,
            s.Name,
            s.Standort,
            s.Land,
            s.IstFreigegeben,
            s.IstAktiv,
            s.ProbeBisUtc is not null && s.ProbeBisUtc > DateTime.UtcNow,
            mitglieder.FirstOrDefault(m => m.Key == s.Id)?.Anzahl ?? 0,
            umsatz.FirstOrDefault(u => u.Key == s.Id)?.Summe ?? 0m,
            s.CreatedAtUtc,
            besitzerNamen.FirstOrDefault(x => x.Key == s.Id)?.Name)).ToList();
    }

    public async Task<bool> SetzeFreigabeAsync(Guid salonId, bool freigegeben, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var salon = await db.Salons.FirstOrDefaultAsync(s => s.Id == salonId, cancellationToken);
        if (salon is null)
        {
            return false;
        }

        salon.IstFreigegeben = freigegeben;
        salon.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetzeAktivAsync(Guid salonId, bool aktiv, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var salon = await db.Salons.FirstOrDefaultAsync(s => s.Id == salonId, cancellationToken);
        if (salon is null)
        {
            return false;
        }

        salon.IstAktiv = aktiv;
        salon.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Admin/PO ändert die Probezeit eines Ladens (verlängern/anpassen/entfernen).</summary>
    public async Task<bool> SetzeProbezeitAsync(Guid salonId, DateTime? probeBisUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var salon = await db.Salons.FirstOrDefaultAsync(s => s.Id == salonId, cancellationToken);
        if (salon is null)
        {
            return false;
        }

        salon.ProbeBisUtc = probeBisUtc;
        salon.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Aktuelle Probezeit eines Ladens lesen.</summary>
    public async Task<DateTime?> GetProbezeitAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Salons
            .AsNoTracking()
            .Where(s => s.Id == salonId)
            .Select(s => s.ProbeBisUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<PlattformPreis>> GetPlattformPreiseAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.PlattformPreise
            .AsNoTracking()
            .Where(p => p.IstAktuell)
            .OrderBy(p => p.Intervall)
            .ToListAsync(cancellationToken);
    }

    public async Task SetzePlattformPreisAsync(AboIntervall intervall, decimal preis, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Bisherige aktuelle Preise des Intervalls historisieren.
        var bisherige = await db.PlattformPreise
            .Where(p => p.Intervall == intervall && p.IstAktuell)
            .ToListAsync(cancellationToken);
        foreach (var alt in bisherige)
        {
            alt.IstAktuell = false;
        }

        db.PlattformPreise.Add(new PlattformPreis
        {
            Intervall = intervall,
            Preis = preis,
            IstAktuell = true,
            GueltigAbUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SetzeIndividuellenPreisAsync(Guid salonId, decimal? preis, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var salon = await db.Salons.FirstOrDefaultAsync(s => s.Id == salonId, cancellationToken);
        if (salon is null)
        {
            return false;
        }

        salon.IndividuellerMonatsPreis = preis;
        salon.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// SuperAdmin legt einen Besitzer samt Geschäft an (freigegeben & aktiv).
    /// Der Besitzer erhält ein Initialpasswort und kann sich sofort anmelden.
    /// </summary>
    public async Task<(bool Erfolg, string? Fehler, AppUser? Besitzer)> LegeBesitzerMitSalonAnAsync(
        string vorname, string nachname, string email, string initialPasswort,
        string salonName, string? standort, string? land = null, CancellationToken cancellationToken = default)
    {
        var bereinigtEmail = email.Trim();
        var bereinigtSalon = salonName.Trim();

        if (await userManager.FindByEmailAsync(bereinigtEmail) is not null)
        {
            return (false, "Un compte avec cette adresse e-mail existe déjà.", null);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Salons.AnyAsync(s => s.Name == bereinigtSalon, cancellationToken))
        {
            return (false, "Un établissement portant ce nom existe déjà.", null);
        }

        var besitzer = new AppUser
        {
            UserName = bereinigtEmail,
            Email = bereinigtEmail,
            Vorname = vorname.Trim(),
            Nachname = nachname.Trim(),
            FullName = $"{vorname.Trim()} {nachname.Trim()}",
            EmailConfirmed = true
        };

        var erstellen = await userManager.CreateAsync(besitzer, initialPasswort);
        if (!erstellen.Succeeded)
        {
            var fehler = string.Join(" ", erstellen.Errors.Select(e => e.Description));
            logger.LogWarning("Admin: Besitzer-Anlage fehlgeschlagen für {Email}: {Fehler}", bereinigtEmail, fehler);
            return (false, string.IsNullOrWhiteSpace(fehler) ? "La création a échoué." : fehler, null);
        }

        await userManager.AddToRoleAsync(besitzer, AppRollen.Besitzer);

        var salon = new Salon
        {
            Name = bereinigtSalon,
            Standort = string.IsNullOrWhiteSpace(standort) ? null : standort.Trim(),
            Land = string.IsNullOrWhiteSpace(land) ? null : land.Trim(),
            IstAktiv = true,
            IstFreigegeben = true, // vom Admin angelegt → sofort freigegeben
            ProbeBisUtc = AuthService.BerechneProbeEnde(DateTime.UtcNow) // Probezeit wie bei Selbstregistrierung
        };

        db.Salons.Add(salon);
        await db.SaveChangesAsync(cancellationToken);

        db.UserSalons.Add(new UserSalon
        {
            AppUserId = besitzer.Id,
            SalonId = salon.Id,
            KannFinanzenSehen = true,
            KannLeistungenVerwalten = true,
            KannRabatteVerwalten = true
        });
        await db.SaveChangesAsync(cancellationToken);

        return (true, null, besitzer);
    }

    /// <summary>SuperAdmin legt einen ProduktOwner an (Initialpasswort, Rolle).</summary>
    public async Task<(bool Erfolg, string? Fehler, AppUser? ProduktOwner)> LegeProduktOwnerAnAsync(
        string vorname, string nachname, string email, string initialPasswort, CancellationToken cancellationToken = default)
    {
        var bereinigtEmail = email.Trim();
        if (await userManager.FindByEmailAsync(bereinigtEmail) is not null)
        {
            return (false, "Un compte avec cette adresse e-mail existe déjà.", null);
        }

        var po = new AppUser
        {
            UserName = bereinigtEmail,
            Email = bereinigtEmail,
            Vorname = vorname.Trim(),
            Nachname = nachname.Trim(),
            FullName = $"{vorname.Trim()} {nachname.Trim()}",
            EmailConfirmed = true
        };

        var erstellen = await userManager.CreateAsync(po, initialPasswort);
        if (!erstellen.Succeeded)
        {
            var fehler = string.Join(" ", erstellen.Errors.Select(e => e.Description));
            logger.LogWarning("Admin: ProduktOwner-Anlage fehlgeschlagen für {Email}: {Fehler}", bereinigtEmail, fehler);
            return (false, string.IsNullOrWhiteSpace(fehler) ? "La création a échoué." : fehler, null);
        }

        await userManager.AddToRoleAsync(po, AppRollen.ProduktOwner);
        return (true, null, po);
    }

    /// <summary>Alle ProduktOwner (für die Admin-Verwaltung).</summary>
    public async Task<List<AppUser>> GetProduktOwnersAsync(CancellationToken cancellationToken = default)
    {
        var poInRolle = await userManager.GetUsersInRoleAsync(AppRollen.ProduktOwner);
        return poInRolle.OrderBy(u => u.FullName).ToList();
    }

    /// <summary>
    /// Salon-Übersicht gefiltert nach Rolle: SuperAdmin sieht alle, ProduktOwner nur
    /// zugewiesene + selbst angelegte Läden.
    /// </summary>
    public async Task<List<SalonUebersicht>> GetSalonUebersichtFuerAsync(
        Guid userId, bool istSuperAdmin, CancellationToken cancellationToken = default)
    {
        var alle = await GetSalonUebersichtAsync(cancellationToken);
        if (istSuperAdmin)
        {
            return alle;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var verwaltet = await db.ProduktOwnerSalons
            .AsNoTracking()
            .Where(p => p.ProduktOwnerId == userId)
            .Select(p => p.SalonId)
            .ToListAsync(cancellationToken);

        var verwaltetSet = verwaltet.ToHashSet();
        return alle.Where(s => verwaltetSet.Contains(s.SalonId)).ToList();
    }
}
