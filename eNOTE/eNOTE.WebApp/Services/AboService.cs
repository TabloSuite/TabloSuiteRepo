using ENote.Core.DataModels.Abo;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Abo-/Nutzungsgebühr-Verwaltung: Besitzer wählt Intervall und meldet die Zahlung;
/// der SuperAdmin bestätigt den Zahlungseingang (Workflow analog SKOULOUAPP).
/// </summary>
public class AboService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    /// <summary>Aktuelles (letztes) Abo eines Salons.</summary>
    public async Task<NutzungsAbo?> GetAktuellesAboAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.NutzungsAbos
            .AsNoTracking()
            .Where(a => a.SalonId == salonId)
            .OrderByDescending(a => a.ZeitraumBisUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Probe-Status eines Salons: läuft die kostenlose Probezeit noch, und bis wann?
    /// </summary>
    public async Task<(bool Laeuft, DateTime? BisUtc)> GetProbeStatusAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var probeBis = await db.Salons
            .AsNoTracking()
            .Where(s => s.Id == salonId)
            .Select(s => s.ProbeBisUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var laeuft = probeBis is not null && probeBis.Value > DateTime.UtcNow;
        return (laeuft, probeBis);
    }

    /// <summary>Effektiver Preis: individueller Salonpreis hat Vorrang vor dem globalen Standard.</summary>
    public async Task<decimal> GetPreisAsync(Guid salonId, AboIntervall intervall, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var salon = await db.Salons.AsNoTracking().FirstOrDefaultAsync(s => s.Id == salonId, cancellationToken);
        if (salon?.IndividuellerMonatsPreis is decimal individuell && intervall == AboIntervall.Monatlich)
        {
            return individuell;
        }

        var standard = await db.PlattformPreise
            .AsNoTracking()
            .Where(p => p.Intervall == intervall && p.IstAktuell)
            .OrderByDescending(p => p.GueltigAbUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return standard?.Preis ?? 0m;
    }

    private static int IntervallMonate(AboIntervall intervall) => intervall switch
    {
        AboIntervall.Monatlich => 1,
        AboIntervall.DreiMonate => 3,
        AboIntervall.Jaehrlich => 12,
        _ => 1
    };

    /// <summary>
    /// Besitzer wählt ein Intervall und meldet die Zahlung. Legt eine Abrechnungsperiode
    /// mit Preis-Snapshot an (Status „Gemeldet“). Startet ab Ende des laufenden Abos oder jetzt.
    /// </summary>
    public async Task<(bool Erfolg, string? Fehler, NutzungsAbo? Abo)> MeldeZahlungAsync(
        Guid salonId, AboIntervall intervall, string? notiz, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var offen = await db.NutzungsAbos
            .AnyAsync(a => a.SalonId == salonId
                && (a.ZahlungStatus == AboZahlungStatus.Offen || a.ZahlungStatus == AboZahlungStatus.Gemeldet),
                cancellationToken);
        if (offen)
        {
            return (false, "Un paiement est déjà en cours pour ce salon.", null);
        }

        var preis = await GetPreisAsync(salonId, intervall, cancellationToken);

        var letztesEnde = await db.NutzungsAbos
            .AsNoTracking()
            .Where(a => a.SalonId == salonId && a.ZahlungStatus == AboZahlungStatus.Bestaetigt)
            .Select(a => (DateTime?)a.ZeitraumBisUtc)
            .OrderByDescending(d => d)
            .FirstOrDefaultAsync(cancellationToken);

        var jetzt = DateTime.UtcNow;
        var von = letztesEnde is DateTime ende && ende > jetzt ? ende : jetzt;
        var bis = von.AddMonths(IntervallMonate(intervall));

        var abo = new NutzungsAbo
        {
            SalonId = salonId,
            Intervall = intervall,
            PreisSnapshot = preis,
            ZeitraumVonUtc = von,
            ZeitraumBisUtc = bis,
            ZahlungStatus = AboZahlungStatus.Gemeldet,
            ZahlungGemeldetUtc = jetzt,
            ZahlungsNotiz = string.IsNullOrWhiteSpace(notiz) ? null : notiz.Trim()
        };

        db.NutzungsAbos.Add(abo);
        await db.SaveChangesAsync(cancellationToken);
        return (true, null, abo);
    }

    /// <summary>SuperAdmin bestätigt oder lehnt eine gemeldete Zahlung.</summary>
    public async Task<bool> SetzeZahlungsStatusAsync(Guid aboId, AboZahlungStatus status, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var abo = await db.NutzungsAbos.FirstOrDefaultAsync(a => a.Id == aboId, cancellationToken);
        if (abo is null)
        {
            return false;
        }

        abo.ZahlungStatus = status;
        if (status == AboZahlungStatus.Bestaetigt)
        {
            abo.ZahlungBestaetigtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Gemeldete (zu bestätigende) Zahlungen aller Salons für den SuperAdmin.</summary>
    public async Task<List<NutzungsAbo>> GetGemeldeteZahlungenAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.NutzungsAbos
            .AsNoTracking()
            .Include(a => a.Salon)
            .Where(a => a.ZahlungStatus == AboZahlungStatus.Gemeldet)
            .OrderBy(a => a.ZahlungGemeldetUtc)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Alle Abos aller Salons (neueste zuerst) für die Facturation-Übersicht.</summary>
    public async Task<List<NutzungsAbo>> GetAlleAbosAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.NutzungsAbos
            .AsNoTracking()
            .Include(a => a.Salon)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
    }
}
