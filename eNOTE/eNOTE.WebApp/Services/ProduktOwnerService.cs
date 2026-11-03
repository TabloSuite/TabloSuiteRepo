using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// ProduktOwner-Verwaltung: Admin legt POs an, weist ihnen Läden zu und steuert
/// deren Umsatz-Sichtbarkeit. POs sehen/verwalten nur zugewiesene + eigene Läden.
/// </summary>
public class ProduktOwnerService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public record Zuweisung(
        Guid ZuweisungId,
        Guid SalonId,
        string SalonName,
        bool KannLadenUmsatzSehen,
        bool KannPlattformUmsatzSehen,
        bool SelbstAngelegt,
        decimal? ProzentProvision,
        decimal? FestbetragProvision);

    /// <summary>Dashboard-Karte eines Ladens für den PO: Umsatz + geschätzte Provision.</summary>
    public record DashboardLaden(
        Guid SalonId,
        string SalonName,
        bool IstFreigegeben,
        decimal UmsatzHeute,
        decimal UmsatzMonat,
        bool KannLadenUmsatzSehen,
        decimal? ProzentProvision,
        decimal? FestbetragProvision,
        decimal AboEinnahmenMonat);

    /// <summary>Alle Zuweisungen eines ProduktOwners.</summary>
    public async Task<List<Zuweisung>> GetZuweisungenAsync(Guid produktOwnerId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ProduktOwnerSalons
            .AsNoTracking()
            .Include(p => p.Salon)
            .Where(p => p.ProduktOwnerId == produktOwnerId)
            .OrderBy(p => p.Salon.Name)
            .Select(p => new Zuweisung(p.Id, p.SalonId, p.Salon.Name, p.KannLadenUmsatzSehen, p.KannPlattformUmsatzSehen, p.SelbstAngelegt, p.ProzentProvision, p.FestbetragProvision))
            .ToListAsync(cancellationToken);
    }

    /// <summary>IDs der Läden, die ein PO verwalten darf.</summary>
    public async Task<HashSet<Guid>> GetVerwalteteSalonIdsAsync(Guid produktOwnerId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ids = await db.ProduktOwnerSalons
            .AsNoTracking()
            .Where(p => p.ProduktOwnerId == produktOwnerId)
            .Select(p => p.SalonId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    /// <summary>Weist einen Laden einem PO zu (oder aktualisiert die Rechte).</summary>
    public async Task<bool> WeiseSalonZuAsync(
        Guid produktOwnerId, Guid salonId, bool kannLadenUmsatz, bool kannPlattformUmsatz,
        bool selbstAngelegt = false, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var vorhanden = await db.ProduktOwnerSalons
            .FirstOrDefaultAsync(p => p.ProduktOwnerId == produktOwnerId && p.SalonId == salonId, cancellationToken);

        if (vorhanden is null)
        {
            db.ProduktOwnerSalons.Add(new ProduktOwnerSalon
            {
                ProduktOwnerId = produktOwnerId,
                SalonId = salonId,
                KannLadenUmsatzSehen = kannLadenUmsatz,
                KannPlattformUmsatzSehen = kannPlattformUmsatz,
                SelbstAngelegt = selbstAngelegt
            });
        }
        else
        {
            vorhanden.KannLadenUmsatzSehen = kannLadenUmsatz;
            vorhanden.KannPlattformUmsatzSehen = kannPlattformUmsatz;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> EntferneZuweisungAsync(Guid zuweisungId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var z = await db.ProduktOwnerSalons.FirstOrDefaultAsync(p => p.Id == zuweisungId, cancellationToken);
        if (z is null)
        {
            return false;
        }

        db.ProduktOwnerSalons.Remove(z);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Admin legt die Provision des PO für einen Laden fest.</summary>
    public async Task<bool> SetzeProvisionAsync(
        Guid zuweisungId, decimal? prozent, decimal? festbetrag, CancellationToken cancellationToken = default)
    {
        if (prozent is < 0 or > 100)
        {
            return false;
        }

        if (festbetrag is < 0)
        {
            return false;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var z = await db.ProduktOwnerSalons.FirstOrDefaultAsync(p => p.Id == zuweisungId, cancellationToken);
        if (z is null)
        {
            return false;
        }

        z.ProzentProvision = prozent;
        z.FestbetragProvision = festbetrag;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Dashboard-Daten für den PO: seine Läden mit Tages-/Monatsumsatz und
    /// geschätzter Provision aus den bestätigten Abo-Zahlungen des Monats.
    /// </summary>
    public async Task<List<DashboardLaden>> GetDashboardAsync(Guid produktOwnerId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var zuweisungen = await db.ProduktOwnerSalons
            .AsNoTracking()
            .Include(p => p.Salon)
            .Where(p => p.ProduktOwnerId == produktOwnerId)
            .ToListAsync(cancellationToken);

        var salonIds = zuweisungen.Select(z => z.SalonId).ToList();
        var heute = DateTime.UtcNow.Date;
        var monatStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        var umsatzHeute = await db.Verkaeufe.AsNoTracking()
            .Where(v => salonIds.Contains(v.SalonId) && v.ZeitUtc >= heute)
            .GroupBy(v => v.SalonId)
            .Select(g => new { g.Key, Summe = g.Sum(v => v.GesamtSumme) })
            .ToListAsync(cancellationToken);

        var umsatzMonat = await db.Verkaeufe.AsNoTracking()
            .Where(v => salonIds.Contains(v.SalonId) && v.ZeitUtc >= monatStart)
            .GroupBy(v => v.SalonId)
            .Select(g => new { g.Key, Summe = g.Sum(v => v.GesamtSumme) })
            .ToListAsync(cancellationToken);

        // Bestätigte Abo-Einnahmen des laufenden Monats je Laden (Plattform-Umsatz).
        var aboEinnahmen = await db.NutzungsAbos.AsNoTracking()
            .Where(a => salonIds.Contains(a.SalonId)
                && a.ZahlungStatus == Core.DataModels.Abo.AboZahlungStatus.Bestaetigt
                && a.ZahlungBestaetigtUtc >= monatStart)
            .GroupBy(a => a.SalonId)
            .Select(g => new { g.Key, Summe = g.Sum(a => a.PreisSnapshot) })
            .ToListAsync(cancellationToken);

        return zuweisungen.Select(z =>
        {
            var abo = aboEinnahmen.FirstOrDefault(x => x.Key == z.SalonId)?.Summe ?? 0m;
            return new DashboardLaden(
                z.SalonId,
                z.Salon.Name,
                z.Salon.IstFreigegeben,
                umsatzHeute.FirstOrDefault(x => x.Key == z.SalonId)?.Summe ?? 0m,
                umsatzMonat.FirstOrDefault(x => x.Key == z.SalonId)?.Summe ?? 0m,
                z.KannLadenUmsatzSehen,
                z.ProzentProvision,
                z.FestbetragProvision,
                abo);
        }).OrderBy(d => d.SalonName).ToList();
    }

    /// <summary>
    /// Berechnet die Provision eines PO für einen Monat aus den bestätigten
    /// Abo-Einnahmen seiner Läden und legt eine offene Auszahlung an (oder liefert
    /// die bereits vorhandene zurück).
    /// </summary>
    public async Task<Core.DataModels.Abo.ProduktOwnerAuszahlung> BerechneAuszahlungAsync(
        Guid produktOwnerId, int jahr, int monat, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var vorhanden = await db.ProduktOwnerAuszahlungen
            .FirstOrDefaultAsync(a => a.ProduktOwnerId == produktOwnerId && a.Jahr == jahr && a.Monat == monat, cancellationToken);
        if (vorhanden is not null)
        {
            return vorhanden;
        }

        var monatStart = new DateTime(jahr, monat, 1, 0, 0, 0, DateTimeKind.Utc);
        var monatEnde = monatStart.AddMonths(1);

        var zuweisungen = await db.ProduktOwnerSalons
            .AsNoTracking()
            .Include(p => p.Salon)
            .Where(p => p.ProduktOwnerId == produktOwnerId)
            .ToListAsync(cancellationToken);

        var salonIds = zuweisungen.Select(z => z.SalonId).ToList();
        var aboEinnahmen = await db.NutzungsAbos
            .AsNoTracking()
            .Where(a => salonIds.Contains(a.SalonId)
                && a.ZahlungStatus == Core.DataModels.Abo.AboZahlungStatus.Bestaetigt
                && a.ZahlungBestaetigtUtc >= monatStart && a.ZahlungBestaetigtUtc < monatEnde)
            .GroupBy(a => a.SalonId)
            .Select(g => new { g.Key, Summe = g.Sum(a => a.PreisSnapshot) })
            .ToListAsync(cancellationToken);

        decimal gesamt = 0m;
        var zeilen = new List<string>();
        foreach (var z in zuweisungen)
        {
            var einnahmen = aboEinnahmen.FirstOrDefault(x => x.Key == z.SalonId)?.Summe ?? 0m;
            if (einnahmen <= 0)
            {
                continue;
            }

            var provision = (z.ProzentProvision is not null ? einnahmen * z.ProzentProvision.Value / 100m : 0m)
                + (z.FestbetragProvision ?? 0m);
            provision = Math.Round(provision, 2);
            if (provision <= 0)
            {
                continue;
            }

            gesamt += provision;
            zeilen.Add($"{z.Salon.Name}: {einnahmen:N0} F → {provision:N0} F");
        }

        var auszahlung = new Core.DataModels.Abo.ProduktOwnerAuszahlung
        {
            ProduktOwnerId = produktOwnerId,
            Jahr = jahr,
            Monat = monat,
            Betrag = gesamt,
            Aufschluesselung = string.Join("\n", zeilen)
        };

        db.ProduktOwnerAuszahlungen.Add(auszahlung);
        await db.SaveChangesAsync(cancellationToken);
        return auszahlung;
    }

    /// <summary>SuperAdmin bestätigt eine Auszahlung.</summary>
    public async Task<bool> BestaetigeAuszahlungAsync(Guid auszahlungId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var auszahlung = await db.ProduktOwnerAuszahlungen.FirstOrDefaultAsync(a => a.Id == auszahlungId, cancellationToken);
        if (auszahlung is null || auszahlung.Status == Core.DataModels.Abo.AuszahlungStatus.Bestaetigt)
        {
            return false;
        }

        auszahlung.Status = Core.DataModels.Abo.AuszahlungStatus.Bestaetigt;
        auszahlung.BestaetigtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Auszahlungen eines Monats (für den SuperAdmin).</summary>
    public async Task<List<Core.DataModels.Abo.ProduktOwnerAuszahlung>> GetAuszahlungenAsync(int jahr, int monat, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ProduktOwnerAuszahlungen
            .AsNoTracking()
            .Include(a => a.ProduktOwner)
            .Where(a => a.Jahr == jahr && a.Monat == monat)
            .OrderByDescending(a => a.Betrag)
            .ToListAsync(cancellationToken);
    }
}
